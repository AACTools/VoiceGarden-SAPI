#pragma once
#include "DataKey.h"
#include "TTSEngine.h"

struct VoiceTraits
{
	static constexpr HKEY RegRoot = HKEY_CURRENT_USER;
	static constexpr std::wstring_view RegPrefix = L"Software\\VoiceGardenSAPIAdapter\\VoiceTokens\\";
	static HRESULT GetStringValueOverride(LPCWSTR pszSubkey, LPCWSTR pszValueName, LPWSTR* ppszValue) noexcept
	{
		// SAPI may query default values with null pointers for subkey/value name.
		// Only override explicit CLSID lookup at token root.
		if (!pszSubkey || !pszValueName)
			return SPERR_NOT_FOUND;
		if (*pszSubkey == L'\0' && _wcsicmp(pszValueName, L"CLSID") == 0)
			return StringFromCLSID(CLSID_TTSEngine, ppszValue);
		return SPERR_NOT_FOUND;
	}
	static constexpr LPCWSTR SpCategory = SPCAT_VOICES;
	// Voice token IDs must live directly under the Tokens branch for stable activation.
	// Nesting under Tokens\\VoiceGardenEnumerator can cause SAPI to attempt reopening
	// non-existent nested registry paths before invoking TTSEngine.
	static constexpr std::wstring_view SpIdRoot = SPCAT_VOICES L"\\Tokens\\";
};

typedef CDataKey<VoiceTraits> CVoiceKey;
