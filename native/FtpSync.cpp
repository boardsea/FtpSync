// Thin native shim: Notepad++ loads this DLL, the shim hosts the .NET Framework 4 CLR
// and forwards everything to FtpSync.Managed.dll (see managed/Plugin.cs).
#include <windows.h>
#include <initguid.h>
#include <mscoree.h>
#include <stddef.h>
#include <stdio.h>

struct NppData { HWND _nppHandle; HWND _scintillaMainHandle; HWND _scintillaSecondHandle; };
typedef void (__cdecl *PFUNCPLUGINCMD)();
struct ShortcutKey { bool _isCtrl; bool _isAlt; bool _isShift; UCHAR _key; };
struct FuncItem {
	wchar_t _itemName[64];
	PFUNCPLUGINCMD _pFunc;
	int _cmdID;
	bool _init2Check;
	ShortcutKey *_pShKey;
};

#define MAXCMD 16

// Shared with managed code (Plugin.cs reads/writes this block by offset).
struct HostTable {
	HWND npp, sci1, sci2;
	int count;
	void (__stdcall *onCommand)(int);
	void (__stdcall *onNotify)(void *);
	wchar_t pluginDir[MAX_PATH];
	wchar_t names[MAXCMD][64];
	void *funcs; // FuncItem array (cmdIDs are assigned by Notepad++)
	int funcStride, cmdIdOffset;
};

static_assert(offsetof(HostTable, count) == 3 * sizeof(void *), "layout");
static_assert(offsetof(HostTable, onCommand) == 4 * sizeof(void *), "layout");
static_assert(offsetof(HostTable, onNotify) == 5 * sizeof(void *), "layout");
static_assert(offsetof(HostTable, pluginDir) == 6 * sizeof(void *), "layout");
static_assert(offsetof(HostTable, names) == 6 * sizeof(void *) + MAX_PATH * 2, "layout");
static_assert(offsetof(HostTable, funcs) == 6 * sizeof(void *) + MAX_PATH * 2 + MAXCMD * 128, "layout");

static HostTable g_host;
static FuncItem g_funcs[MAXCMD];
static HMODULE g_module;
static bool g_loaded;

static void run(int i) { if (g_host.onCommand) g_host.onCommand(i); }
#define CMD(n) static void __cdecl cmd##n() { run(n); }
CMD(0) CMD(1) CMD(2) CMD(3) CMD(4) CMD(5) CMD(6) CMD(7)
CMD(8) CMD(9) CMD(10) CMD(11) CMD(12) CMD(13) CMD(14) CMD(15)
static PFUNCPLUGINCMD g_cmds[MAXCMD] = { cmd0, cmd1, cmd2, cmd3, cmd4, cmd5, cmd6, cmd7,
	cmd8, cmd9, cmd10, cmd11, cmd12, cmd13, cmd14, cmd15 };

typedef HRESULT (WINAPI *CorBindToRuntimeExFn)(LPCWSTR, LPCWSTR, DWORD, REFCLSID, REFIID, LPVOID *);

static void fail(const wchar_t *msg) {
	MessageBoxW(g_host.npp, msg, L"FTP Sync", MB_ICONERROR | MB_OK);
}

static void loadManaged() {
	if (g_loaded) return;
	g_loaded = true;

	wchar_t path[MAX_PATH];
	GetModuleFileNameW(g_module, path, MAX_PATH);
	wchar_t *slash = wcsrchr(path, L'\\');
	if (slash) *slash = 0;
	wcsncpy(g_host.pluginDir, path, MAX_PATH - 1);

	wchar_t asmPath[MAX_PATH + 40];
	swprintf(asmPath, MAX_PATH + 40, L"%ls\\FtpSync.Managed.dll", path);

	HMODULE mscoree = LoadLibraryW(L"mscoree.dll");
	if (!mscoree) { fail(L".NET Framework 4.x is required (mscoree.dll not found)."); return; }
	CorBindToRuntimeExFn bind = (CorBindToRuntimeExFn)GetProcAddress(mscoree, "CorBindToRuntimeEx");
	if (!bind) { fail(L"CorBindToRuntimeEx not found."); return; }

	ICLRRuntimeHost *host = NULL;
	HRESULT hr = bind(L"v4.0.30319", L"wks", 0,
		CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID *)&host);
	if (FAILED(hr) || !host) { fail(L"Cannot load .NET Framework 4 runtime."); return; }
	host->Start(); // S_FALSE when already started by another plugin: fine

	wchar_t arg[64];
	swprintf(arg, 64, L"%llu", (unsigned long long)(ULONG_PTR)&g_host);
	DWORD ret = 0;
	hr = host->ExecuteInDefaultAppDomain(asmPath, L"FtpSync.Plugin", L"Bootstrap", arg, &ret);
	if (FAILED(hr) || ret != 1) {
		wchar_t m[300];
		swprintf(m, 300, L"Cannot start FtpSync.Managed.dll (hr=0x%08lX, ret=%lu).", (unsigned long)hr, (unsigned long)ret);
		fail(m);
		return;
	}
	g_host.funcs = g_funcs;
	g_host.funcStride = (int)sizeof(FuncItem);
	g_host.cmdIdOffset = (int)offsetof(FuncItem, _cmdID);
	for (int i = 0; i < g_host.count && i < MAXCMD; i++) {
		wcsncpy(g_funcs[i]._itemName, g_host.names[i], 63);
		g_funcs[i]._pFunc = g_cmds[i];
		g_funcs[i]._cmdID = 0;
		g_funcs[i]._init2Check = false;
		g_funcs[i]._pShKey = NULL;
	}
}

extern "C" {
__declspec(dllexport) void setInfo(NppData data) {
	g_host.npp = data._nppHandle;
	g_host.sci1 = data._scintillaMainHandle;
	g_host.sci2 = data._scintillaSecondHandle;
	loadManaged();
}
__declspec(dllexport) const wchar_t *getName() { return L"FTP Sync"; }
__declspec(dllexport) FuncItem *getFuncsArray(int *nbF) { *nbF = g_host.count; return g_funcs; }
__declspec(dllexport) void beNotified(void *notification) { if (g_host.onNotify) g_host.onNotify(notification); }
__declspec(dllexport) LRESULT messageProc(UINT, WPARAM, LPARAM) { return TRUE; }
__declspec(dllexport) BOOL isUnicode() { return TRUE; }
}

BOOL APIENTRY DllMain(HMODULE h, DWORD reason, LPVOID) {
	if (reason == DLL_PROCESS_ATTACH) { g_module = h; DisableThreadLibraryCalls(h); }
	return TRUE;
}
