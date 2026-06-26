// VoiceGardenSAPIAdapter.cpp: DLL 导出的实现。


#include "pch.h"
#include "framework.h"
#include "resource.h"
#include "VoiceGardenSAPIAdapter_i.h"
#include "dllmain.h"
#include <string>


using namespace ATL;

WCHAR g_regModulePath[MAX_PATH];
_ATL_REGMAP_ENTRY g_regEntries[] = { {L"ModulePath", g_regModulePath}, {nullptr, nullptr} };
static constexpr wchar_t kTtsEngineClsid[] = L"{013ab33b-ad1a-401c-8bee-f6e2b046a94e}";
static constexpr wchar_t kVoiceTokenEnumeratorClsid[] = L"{b8b9e38f-e5a2-4661-9fde-4ac7377aa6f6}";

static HRESULT GetRegModulePath()
{
	WCHAR path[MAX_PATH];
	DWORD len = GetModuleFileNameW((HMODULE)&__ImageBase, path, MAX_PATH);
	if (len == 0)
		return AtlHresultFromLastError();
	else if (len == MAX_PATH)
		return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
#ifdef _M_ARM64
	// Replace the module path in the registry with Arm64XForwarder,
	// only in the ARM64 version.
	PathRemoveFileSpecW(path);
	if (!PathAppendW(path, L"Arm64XForwarder.dll"))
		return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
#endif
	CAtlModule::EscapeSingleQuote(g_regModulePath, MAX_PATH, path);
	return S_OK;
}

static HRESULT GetModulePathRaw(std::wstring& modulePath)
{
	WCHAR path[MAX_PATH];
	DWORD len = GetModuleFileNameW((HMODULE)&__ImageBase, path, MAX_PATH);
	if (len == 0)
		return AtlHresultFromLastError();
	if (len == MAX_PATH)
		return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);

#ifdef _M_ARM64
	PathRemoveFileSpecW(path);
	if (!PathAppendW(path, L"Arm64XForwarder.dll"))
		return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
#endif
	modulePath = path;
	return S_OK;
}

static HRESULT WriteRegString(HKEY root, const std::wstring& subkey, LPCWSTR name, const std::wstring& value)
{
	HKEY hKey = nullptr;
	LSTATUS st = RegCreateKeyExW(root, subkey.c_str(), 0, nullptr, REG_OPTION_NON_VOLATILE,
		KEY_SET_VALUE, nullptr, &hKey, nullptr);
	if (st != ERROR_SUCCESS)
		return HRESULT_FROM_WIN32(st);

	st = RegSetValueExW(hKey, name, 0, REG_SZ,
		reinterpret_cast<const BYTE*>(value.c_str()),
		static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
	RegCloseKey(hKey);
	return HRESULT_FROM_WIN32(st);
}

static HRESULT EnsureFallbackRegistration()
{
	std::wstring modulePath;
	HRESULT hr = GetModulePathRaw(modulePath);
	if (FAILED(hr))
		return hr;

	// COM class registration
	hr = WriteRegString(HKEY_CLASSES_ROOT,
		std::wstring(L"CLSID\\") + kTtsEngineClsid + L"\\InprocServer32",
		nullptr, modulePath);
	if (FAILED(hr)) return hr;
	hr = WriteRegString(HKEY_CLASSES_ROOT,
		std::wstring(L"CLSID\\") + kTtsEngineClsid + L"\\InprocServer32",
		L"ThreadingModel", L"Both");
	if (FAILED(hr)) return hr;

	hr = WriteRegString(HKEY_CLASSES_ROOT,
		std::wstring(L"CLSID\\") + kVoiceTokenEnumeratorClsid + L"\\InprocServer32",
		nullptr, modulePath);
	if (FAILED(hr)) return hr;
	hr = WriteRegString(HKEY_CLASSES_ROOT,
		std::wstring(L"CLSID\\") + kVoiceTokenEnumeratorClsid + L"\\InprocServer32",
		L"ThreadingModel", L"Both");
	if (FAILED(hr)) return hr;

	// SAPI TokenEnums hook
	hr = WriteRegString(HKEY_LOCAL_MACHINE,
		L"SOFTWARE\\Microsoft\\Speech\\Voices\\TokenEnums\\VoiceGardenEnumerator",
		L"CLSID", kVoiceTokenEnumeratorClsid);
	if (FAILED(hr)) return hr;

	return S_OK;
}

static void RemoveFallbackRegistration()
{
	RegDeleteTreeW(HKEY_CLASSES_ROOT, (std::wstring(L"CLSID\\") + kTtsEngineClsid).c_str());
	RegDeleteTreeW(HKEY_CLASSES_ROOT, (std::wstring(L"CLSID\\") + kVoiceTokenEnumeratorClsid).c_str());
	RegDeleteTreeW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Speech\\Voices\\TokenEnums\\VoiceGardenEnumerator");
}

// 用于确定 DLL 是否可由 OLE 卸载。
_Use_decl_annotations_
STDAPI DllCanUnloadNow(void)
{
	return _AtlModule.DllCanUnloadNow();
}

// 返回一个类工厂以创建所请求类型的对象。
_Use_decl_annotations_
STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _Outptr_ LPVOID* ppv)
{
	return _AtlModule.DllGetClassObject(rclsid, riid, ppv);
}

// DllRegisterServer - 向系统注册表中添加项。
_Use_decl_annotations_
STDAPI DllRegisterServer(void)
{
	// 注册对象、类型库和类型库中的所有接口
	HRESULT hr = GetRegModulePath();
	if (FAILED(hr))
		return hr;
	hr = _AtlModule.DllRegisterServer();
	if (FAILED(hr))
		return hr;
	return EnsureFallbackRegistration();
}

// DllUnregisterServer - 移除系统注册表中的项。
_Use_decl_annotations_
STDAPI DllUnregisterServer(void)
{
	HRESULT hr = GetRegModulePath();
	if (FAILED(hr))
		return hr;
	RemoveFallbackRegistration();
	hr = _AtlModule.DllUnregisterServer();
	return hr;
}

// DllInstall - 按用户和计算机在系统注册表中逐一添加/移除项。
STDAPI DllInstall(BOOL bInstall, _In_opt_  LPCWSTR pszCmdLine)
{
	HRESULT hr = E_FAIL;
	static const wchar_t szUserSwitch[] = L"user";

	if (pszCmdLine != nullptr)
	{
		if (_wcsnicmp(pszCmdLine, szUserSwitch, _countof(szUserSwitch)) == 0)
		{
			ATL::AtlSetPerUserRegistration(true);
		}
	}

	if (bInstall)
	{
		hr = DllRegisterServer();
		if (FAILED(hr))
		{
			DllUnregisterServer();
		}
	}
	else
	{
		hr = DllUnregisterServer();
	}

	return hr;
}


