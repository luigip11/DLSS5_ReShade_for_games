// GamePanel contributors, MIT. Synthetic DX9 x86 renderer; no game assets or DRM.
#include <windows.h>
#include <d3d9.h>

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM w, LPARAM l)
{
    if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProc(window, message, w, l);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    WNDCLASS definition = {};
    definition.lpfnWndProc = WindowProcedure;
    definition.hInstance = instance;
    definition.lpszClassName = L"GamePanelGraphicsFixture";
    if (!RegisterClass(&definition)) return 2;
    HWND window = CreateWindow(definition.lpszClassName, L"GamePanel DX9 render fixture", WS_OVERLAPPEDWINDOW,
        100, 100, 640, 480, nullptr, nullptr, instance, nullptr);
    if (!window) return 3;
    ShowWindow(window, SW_SHOW);
    IDirect3D9* api = Direct3DCreate9(D3D_SDK_VERSION);
    if (!api) return 77;
    D3DPRESENT_PARAMETERS presentation = {};
    presentation.Windowed = TRUE;
    presentation.SwapEffect = D3DSWAPEFFECT_DISCARD;
    presentation.hDeviceWindow = window;
    presentation.BackBufferFormat = D3DFMT_UNKNOWN;
    presentation.PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE;
    IDirect3DDevice9* device = nullptr;
    HRESULT result = api->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, window,
        D3DCREATE_SOFTWARE_VERTEXPROCESSING, &presentation, &device);
    if (FAILED(result)) { api->Release(); DestroyWindow(window); return 77; }
    MSG message = {};
    for (int frame = 0; frame < 360; ++frame)
    {
        while (PeekMessage(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessage(&message); }
        if (message.message == WM_QUIT) break;
        if (FAILED(device->Clear(0, nullptr, D3DCLEAR_TARGET, D3DCOLOR_XRGB(85, 110, 140), 1.0f, 0))) { result = E_FAIL; break; }
        if (FAILED(device->BeginScene())) { result = E_FAIL; break; }
        device->EndScene();
        result = device->Present(nullptr, nullptr, nullptr, nullptr);
        if (FAILED(result)) break;
        Sleep(16);
    }
    device->Release(); api->Release(); DestroyWindow(window);
    return SUCCEEDED(result) ? 0 : 4;
}
