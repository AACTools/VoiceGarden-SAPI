#include "framework.h"
#include "Installer.h"
#include "CloudEnginesDlg.h"
#include <thread>
#include <nlohmann/json.hpp>

namespace {

struct CloudVoiceItem {
    std::wstring id;
    std::wstring name;
    std::wstring language;
    std::wstring gender;
    bool selected = false;
};

struct CloudDialogState {
    std::wstring engineConfigPath;
    std::vector<CloudVoiceItem> voices;
    std::wstring selectedEngine = L"openai";
    bool busy = false;
};

constexpr UINT WM_CLOUD_FETCH_DONE = WM_APP + 301;

std::wstring GetDlgItemTextW(HWND hDlg, int id) {
    wchar_t buf[1024] = {};
    ::GetDlgItemTextW(hDlg, id, buf, 1024);
    return buf;
}

void AppendLog(HWND hDlg, const std::wstring& text) {
    HWND hLog = GetDlgItem(hDlg, IDC_CLOUD_LOG);
    int len = GetWindowTextLengthW(hLog);
    SendMessageW(hLog, EM_SETSEL, len, len);
    SendMessageW(hLog, EM_REPLACESEL, FALSE, (LPARAM)text.c_str());
    SendMessageW(hLog, EM_SCROLLCARET, 0, 0);
}

void SetStatus(HWND hDlg, const std::wstring& text) {
    SetDlgItemTextW(hDlg, IDC_CLOUD_STATUS, text.c_str());
}

void SetBusy(HWND hDlg, CloudDialogState* st, bool busy) {
    st->busy = busy;
    EnableWindow(GetDlgItem(hDlg, IDC_CLOUD_FETCH), !busy);
    EnableWindow(GetDlgItem(hDlg, IDC_CLOUD_APPLY), !busy);
    EnableWindow(GetDlgItem(hDlg, IDC_CLOUD_VALIDATE), !busy);
    EnableWindow(GetDlgItem(hDlg, IDC_CLOUD_ENGINE_COMBO), !busy);
}

bool FindEngineConfigPath(std::wstring& path) {
    wchar_t modulePath[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, modulePath, MAX_PATH);
    PathRemoveFileSpecW(modulePath);
    std::wstring dir = modulePath;

    std::vector<std::wstring> candidates = {
        dir + L"\\EngineConfig.exe",
        dir + L"\\x64\\EngineConfig.exe",
        dir + L"\\x86\\EngineConfig.exe",
        dir + L"\\..\\x64\\EngineConfig.exe",
        dir + L"\\..\\x86\\EngineConfig.exe",
        dir + L"\\..\\engine-config\\EngineConfig.exe",
        dir + L"\\..\\EngineConfig\\bin\\publish\\EngineConfig.exe",
        dir + L"\\..\\EngineConfig\\bin\\Release\\net8.0\\EngineConfig.exe",
    };

    for (const auto& c : candidates) {
        if (PathFileExistsW(c.c_str())) {
            path = c;
            return true;
        }
    }
    return false;
}

// Capture stdout from a process and return the output string
std::wstring RunProcessCaptureOutput(const std::wstring& exePath, const std::wstring& args) {
    SECURITY_ATTRIBUTES sa = { sizeof(sa) };
    sa.bInheritHandle = TRUE;

    HANDLE hReadPipe = nullptr, hWritePipe = nullptr;
    CreatePipe(&hReadPipe, &hWritePipe, &sa, 0);
    SetHandleInformation(hReadPipe, HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOW si = { sizeof(si) };
    si.dwFlags = STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES;
    si.wShowWindow = SW_HIDE;
    si.hStdOutput = hWritePipe;
    si.hStdError = hWritePipe;

    PROCESS_INFORMATION pi = {};

    std::wstring cmdLine = L"\"" + exePath + L"\" " + args;

    std::string output;
    if (CreateProcessW(nullptr, cmdLine.data(), nullptr, nullptr, TRUE, 0, nullptr, nullptr, &si, &pi)) {
        CloseHandle(hWritePipe);

        char buf[4096];
        DWORD bytesRead;
        while (ReadFile(hReadPipe, buf, sizeof(buf), &bytesRead, nullptr) && bytesRead > 0) {
            output.append(buf, bytesRead);
        }

        WaitForSingleObject(pi.hProcess, 30000);
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
    }

    CloseHandle(hReadPipe);
    if (hWritePipe) CloseHandle(hWritePipe);

    // Convert UTF-8 to wide
    if (output.empty()) return L"";
    int wlen = MultiByteToWideChar(CP_UTF8, 0, output.c_str(), (int)output.size(), nullptr, 0);
    std::wstring result(wlen, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, output.c_str(), (int)output.size(), result.data(), wlen);
    if (!result.empty() && result.back() == L'\0') result.pop_back();
    return result;
}

DWORD RunProcessElevated(const std::wstring& exePath, const std::wstring& args) {
    SHELLEXECUTEINFOW info = { sizeof(info) };
    info.fMask = SEE_MASK_NOCLOSEPROCESS;
    info.lpFile = exePath.c_str();
    info.lpParameters = args.c_str();
    info.nShow = SW_HIDE;
    if (!IsAdmin() && SupportsUAC())
        info.lpVerb = L"runas";

    if (!ShellExecuteExW(&info))
        throw std::system_error(GetLastError(), std::system_category());

    DWORD exitCode = 0;
    if (info.hProcess) {
        while (WaitForSingleObject(info.hProcess, 100) == WAIT_TIMEOUT) {
            MSG msg;
            while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
                if (!IsDialogMessageW(GetParent(GetActiveWindow()), &msg)) {
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }
        }
        GetExitCodeProcess(info.hProcess, &exitCode);
        CloseHandle(info.hProcess);
    }
    return exitCode;
}

void PopulateVoiceList(HWND hDlg, CloudDialogState* st) {
    HWND hList = GetDlgItem(hDlg, IDC_CLOUD_VOICES);
    SendMessageW(hList, LB_RESETCONTENT, 0, 0);
    for (size_t i = 0; i < st->voices.size(); i++) {
        auto& v = st->voices[i];
        std::wstring display = v.name + L" (" + v.language + L")";
        if (!v.gender.empty()) display += L" [" + v.gender + L"]";
        int idx = (int)SendMessageW(hList, LB_ADDSTRING, 0, (LPARAM)display.c_str());
        SendMessageW(hList, LB_SETITEMDATA, idx, (LPARAM)i);
    }
    SetStatus(hDlg, L"Found " + std::to_wstring(st->voices.size()) + L" voices");
}

void OnFetchVoices(HWND hDlg, CloudDialogState* st) {
    auto key = GetDlgItemTextW(hDlg, IDC_CLOUD_KEY);
    auto region = GetDlgItemTextW(hDlg, IDC_CLOUD_REGION);

    if (key.empty()) {
        MessageBoxW(hDlg, L"Enter an API key first.", L"Cloud Engines", MB_ICONWARNING);
        return;
    }

    SetBusy(hDlg, st, true);
    SetStatus(hDlg, L"Fetching voices...");
    AppendLog(hDlg, L"\r\nFetching voices for " + st->selectedEngine + L"...\r\n");

    std::wstring engine = st->selectedEngine;
    std::wstring exePath = st->engineConfigPath;

    std::thread([hDlg, exePath, engine, key, region]() {
        std::wstring args = L"voices --engine " + engine + L" --key \"" + key + L"\"";
        if (!region.empty()) args += L" --region " + region;
        args += L" --json";

        auto output = RunProcessCaptureOutput(exePath, args);
        PostMessageW(hDlg, WM_CLOUD_FETCH_DONE, 0, (LPARAM)new std::wstring(std::move(output)));
    }).detach();
}

void OnFetchDone(HWND hDlg, CloudDialogState* st, std::wstring* output) {
    std::unique_ptr<std::wstring> outputPtr(output);
    SetBusy(hDlg, st, false);

    if (outputPtr->empty()) {
        AppendLog(hDlg, L"Error: No output from EngineConfig\r\n");
        SetStatus(hDlg, L"Fetch failed");
        return;
    }

    // Validation results contain "valid" — show as text, don't parse as JSON
    std::string narrow(outputPtr->begin(), outputPtr->end());
    if (narrow.find("Credentials valid") != std::string::npos ||
        narrow.find("Credentials invalid") != std::string::npos ||
        narrow.find("Validating") != std::string::npos) {
        AppendLog(hDlg, *outputPtr + L"\r\n");
        SetStatus(hDlg, narrow.find("valid!") != std::string::npos ? L"Valid" : L"Invalid");
        return;
    }

    // Extract JSON array from output (skip any non-JSON prefix lines)
    size_t jsonStart = narrow.find('[');
    if (jsonStart == std::string::npos) {
        AppendLog(hDlg, L"No voice data found\r\n");
        SetStatus(hDlg, L"No voices");
        return;
    }

    st->voices.clear();
    try {
        auto json = nlohmann::json::parse(narrow.substr(jsonStart));
        for (const auto& v : json) {
            CloudVoiceItem item;
            auto id = v.value("id", "");
            auto name = v.value("name", id);
            auto lang = v.value("language", "en-US");
            auto gender = v.value("gender", "");

            item.id = std::wstring(id.begin(), id.end());
            item.name = std::wstring(name.begin(), name.end());
            item.language = std::wstring(lang.begin(), lang.end());
            item.gender = std::wstring(gender.begin(), gender.end());

            if (item.id.empty()) item.id = item.name;
            st->voices.push_back(std::move(item));
        }
        PopulateVoiceList(hDlg, st);
        AppendLog(hDlg, L"Fetched " + std::to_wstring(st->voices.size()) + L" voices\r\n");
    }
    catch (const std::exception& ex) {
        AppendLog(hDlg, L"Error parsing voices: " + std::wstring(ex.what(), ex.what() + strlen(ex.what())) + L"\r\n");
        SetStatus(hDlg, L"Parse error");
    }
}

void OnApply(HWND hDlg, CloudDialogState* st) {
    HWND hList = GetDlgItem(hDlg, IDC_CLOUD_VOICES);
    int selCount = 0;
    std::vector<int> selectedIndices;

    for (int i = 0; i < (int)SendMessageW(hList, LB_GETCOUNT, 0, 0); i++) {
        if (SendMessageW(hList, LB_GETSEL, i, 0) > 0) {
            selectedIndices.push_back(i);
            selCount++;
        }
    }

    if (selCount == 0) {
        MessageBoxW(hDlg, L"Select at least one voice to install.", L"Cloud Engines", MB_ICONINFORMATION);
        return;
    }

    auto key = GetDlgItemTextW(hDlg, IDC_CLOUD_KEY);
    auto region = GetDlgItemTextW(hDlg, IDC_CLOUD_REGION);

    SetBusy(hDlg, st, true);
    AppendLog(hDlg, L"\r\nInstalling " + std::to_wstring(selCount) + L" voice(s) to HKLM...\r\n");

    for (int idx : selectedIndices) {
        auto voiceIdx = SendMessageW(hList, LB_GETITEMDATA, idx, 0);
        if (voiceIdx < 0 || voiceIdx >= (LPARAM)st->voices.size()) continue;
        auto& voice = st->voices[voiceIdx];

        std::wstring cmd = L"promote --engine " + st->selectedEngine +
            L" --voice \"" + voice.id + L"\" --key \"" + key + L"\"";
        if (!region.empty()) cmd += L" --region " + region;

        try {
            DWORD rc = RunProcessElevated(st->engineConfigPath, cmd);
            if (rc == 0) {
                AppendLog(hDlg, L"Installed: " + voice.name + L"\r\n");
            } else {
                AppendLog(hDlg, L"Failed: " + voice.name + L" (exit " + std::to_wstring(rc) + L")\r\n");
            }
        }
        catch (const std::exception& ex) {
            AppendLog(hDlg, L"Error: " + std::wstring(ex.what(), ex.what() + strlen(ex.what())) + L"\r\n");
        }
    }

    AppendLog(hDlg, L"HKLM token installation complete.\r\n");
    SetStatus(hDlg, L"Done");
    SetBusy(hDlg, st, false);
}

void OnValidate(HWND hDlg, CloudDialogState* st) {
    auto key = GetDlgItemTextW(hDlg, IDC_CLOUD_KEY);
    auto region = GetDlgItemTextW(hDlg, IDC_CLOUD_REGION);

    if (key.empty()) {
        MessageBoxW(hDlg, L"Enter an API key first.", L"Cloud Engines", MB_ICONWARNING);
        return;
    }

    SetBusy(hDlg, st, true);
    SetStatus(hDlg, L"Validating...");

    std::wstring engine = st->selectedEngine;
    std::wstring exePath = st->engineConfigPath;

    std::thread([hDlg, exePath, engine, key, region]() {
        std::wstring args = L"validate --engine " + engine + L" --key \"" + key + L"\"";
        if (!region.empty()) args += L" --region " + region;
        auto output = RunProcessCaptureOutput(exePath, args);
        PostMessageW(hDlg, WM_CLOUD_FETCH_DONE, 0, (LPARAM)new std::wstring(std::move(output)));
    }).detach();

    AppendLog(hDlg, L"\r\nValidating " + engine + L" credentials...\r\n");
}

} // namespace

INT_PTR CALLBACK CloudEnginesDlg(HWND hDlg, UINT message, WPARAM wParam, LPARAM lParam) {
    UNREFERENCED_PARAMETER(lParam);

    switch (message) {
    case WM_INITDIALOG: {
        auto* st = new CloudDialogState();
        SetWindowLongPtrW(hDlg, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(st));

        if (!FindEngineConfigPath(st->engineConfigPath)) {
            MessageBoxW(hDlg, L"EngineConfig.exe was not found.", L"Cloud Engines", MB_ICONEXCLAMATION);
            EndDialog(hDlg, IDCANCEL);
            return TRUE;
        }

        // Populate engine dropdown — all engines including Azure and Edge
        const wchar_t* engines[] = {
            L"azure", L"openai", L"elevenlabs", L"google", L"polly", L"cartesia", L"deepgram"
        };
        for (auto e : engines) {
            SendMessageW(GetDlgItem(hDlg, IDC_CLOUD_ENGINE_COMBO), CB_ADDSTRING, 0, (LPARAM)e);
        }
        SendMessageW(GetDlgItem(hDlg, IDC_CLOUD_ENGINE_COMBO), CB_SETCURSEL, 0, 0);

        // Pre-fill Azure key from existing registry if available
        {
            RegKey key;
            if (key.Open(HKEY_CURRENT_USER, L"Software\\NaturalVoiceSAPIAdapter\\Enumerator", KEY_QUERY_VALUE)) {
                auto azureKey = key.GetString(L"AzureVoiceKey");
                auto azureRegion = key.GetString(L"AzureVoiceRegion");
                if (!azureKey.empty()) {
                    SetDlgItemTextW(hDlg, IDC_CLOUD_KEY, azureKey.c_str());
                }
                if (!azureRegion.empty()) {
                    SetDlgItemTextW(hDlg, IDC_CLOUD_REGION, azureRegion.c_str());
                }
            }
        }

        AppendLog(hDlg, L"Cloud Engines manager loaded.\r\n");
        return TRUE;
    }

    case WM_CLOUD_FETCH_DONE: {
        auto* st = reinterpret_cast<CloudDialogState*>(GetWindowLongPtrW(hDlg, GWLP_USERDATA));
        if (!st) return TRUE;

        auto* output = reinterpret_cast<std::wstring*>(lParam);

        // Check if it's validate output (contains "valid" or "invalid") vs voice JSON
        if (output->find(L"valid") != std::wstring::npos || output->find(L"invalid") != std::wstring::npos) {
            // Validation result
            SetBusy(hDlg, st, false);
            AppendLog(hDlg, *output + L"\r\n");
            SetStatus(hDlg, output->find(L"valid!") != std::wstring::npos ? L"Valid" : L"Invalid");
            delete output;
        } else {
            // Voice list JSON
            OnFetchDone(hDlg, st, output);
        }
        return TRUE;
    }

    case WM_COMMAND: {
        auto* st = reinterpret_cast<CloudDialogState*>(GetWindowLongPtrW(hDlg, GWLP_USERDATA));
        if (!st) break;

        switch (LOWORD(wParam)) {
        case IDC_CLOUD_ENGINE_COMBO:
            if (HIWORD(wParam) == CBN_SELCHANGE) {
                int sel = (int)SendDlgItemMessageW(hDlg, IDC_CLOUD_ENGINE_COMBO, CB_GETCURSEL, 0, 0);
                wchar_t buf[64] = {};
                SendDlgItemMessageW(hDlg, IDC_CLOUD_ENGINE_COMBO, CB_GETLBTEXT, sel, (LPARAM)buf);
                st->selectedEngine = buf;

                // Show region field for engines that need it
                bool needsRegion = (st->selectedEngine == L"azure" || st->selectedEngine == L"polly");
                ShowWindow(GetDlgItem(hDlg, IDC_CLOUD_REGION), needsRegion ? SW_SHOW : SW_HIDE);

                // Pre-fill Azure key from registry when Azure selected
                if (st->selectedEngine == L"azure") {
                    RegKey key;
                    if (key.Open(HKEY_CURRENT_USER, L"Software\\NaturalVoiceSAPIAdapter\\Enumerator", KEY_QUERY_VALUE)) {
                        auto k = key.GetString(L"AzureVoiceKey");
                        auto r = key.GetString(L"AzureVoiceRegion");
                        if (!k.empty()) SetDlgItemTextW(hDlg, IDC_CLOUD_KEY, k.c_str());
                        if (!r.empty()) SetDlgItemTextW(hDlg, IDC_CLOUD_REGION, r.c_str());
                    }
                }
            }
            break;

        case IDC_CLOUD_FETCH:
            if (!st->busy) OnFetchVoices(hDlg, st);
            break;

        case IDC_CLOUD_VALIDATE:
            if (!st->busy) OnValidate(hDlg, st);
            break;

        case IDC_CLOUD_APPLY:
            if (!st->busy) OnApply(hDlg, st);
            break;

        case IDOK:
        case IDCANCEL:
            delete st;
            EndDialog(hDlg, LOWORD(wParam));
            return TRUE;
        }
        break;
    }

    case WM_DESTROY: {
        auto* st = reinterpret_cast<CloudDialogState*>(GetWindowLongPtrW(hDlg, GWLP_USERDATA));
        delete st;
        SetWindowLongPtrW(hDlg, GWLP_USERDATA, 0);
        break;
    }
    }
    return FALSE;
}
