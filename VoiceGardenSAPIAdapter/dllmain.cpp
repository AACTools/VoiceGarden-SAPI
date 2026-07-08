// dllmain.cpp: DllMain implementation.

#include "pch.h"
#include "framework.h"
#include "resource.h"
#include "VoiceGardenSAPIAdapter_i.h"
#include "dllmain.h"
#include "TaskScheduler.h"

CVoiceGardenSAPIAdapterModule _AtlModule;

TaskScheduler g_taskScheduler;

void InitializeLogger() noexcept;
void UninitializeLogger() noexcept;

extern "C" BOOL WINAPI DllMain(HINSTANCE hInstance, DWORD dwReason, LPVOID lpReserved)
{
	hInstance;

	if (dwReason == DLL_PROCESS_ATTACH)
	{
		InitializeLogger();
	}
	else if (dwReason == DLL_PROCESS_DETACH)
	{
		UninitializeLogger();
		if (lpReserved == nullptr)  // being unloaded dynamically
		{
			g_taskScheduler.Uninitialize(true);
		}
		else
		{
			g_taskScheduler.Uninitialize(false);
		}
	}

	return _AtlModule.DllMain(dwReason, lpReserved);
}
