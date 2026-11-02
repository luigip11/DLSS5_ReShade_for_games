// GamePanel contributors, MIT. Synthetic Direct3D 9 test; no game executable or assets.
// Hash constants/algorithm reproduce Texture Toolkit v1.2.1 (BadassBaboon, MIT).
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d9.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <iterator>
#include <string>

namespace
{
    constexpr UINT width = 128, height = 128, texture_size = 64;
    constexpr DWORD original_color = 0xFF4080C0;
    constexpr DWORD replacement_color = 0xFF20E040;

    LRESULT CALLBACK window_proc(HWND window, UINT message, WPARAM w, LPARAM l)
    {
        if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
        return DefWindowProcW(window, message, w, l);
    }

    uint64_t texture_hash()
    {
        const uint64_t word = static_cast<uint64_t>(original_color) |
                              (static_cast<uint64_t>(original_color) << 32);
        uint64_t h = 0x27D4EB2F165667C5ULL;
        for (size_t i = 0; i < static_cast<size_t>(texture_size) * texture_size * 4; i += 8)
        {
            h ^= word * 0x9E3779B185EBCA87ULL;
            h = (h << 31) | (h >> 33);
            h *= 0xC2B2AE3D27D4EB4FULL;
        }
        h ^= static_cast<uint64_t>(texture_size) * texture_size * 4;
        h ^= h >> 33; h *= 0xFF51AFD7ED558CCDULL;
        h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53ULL;
        h ^= h >> 33;
        return h;
    }

    void result(const char* status, bool toolkit, bool reshade, DWORD pixel, HRESULT hr)
    {
        FILE* file = nullptr;
        if (fopen_s(&file, "fixture-result.json", "wb") != 0 || !file) return;
        fprintf(file, "{\"status\":\"%s\",\"toolkitLoaded\":%s,\"reshadeLoaded\":%s,"
                      "\"originalTextureHash\":\"%016llX\",\"rgb\":[%u,%u,%u],\"hresult\":\"%08lX\"}\n",
                status, toolkit ? "true" : "false", reshade ? "true" : "false",
                static_cast<unsigned long long>(texture_hash()),
                static_cast<unsigned>((pixel >> 16) & 255),
                static_cast<unsigned>((pixel >> 8) & 255),
                static_cast<unsigned>(pixel & 255), static_cast<unsigned long>(hr));
        fclose(file);
    }

    bool close_channel(DWORD actual, DWORD expected)
    {
        for (int shift : { 0, 8, 16 })
        {
            const int difference = static_cast<int>((actual >> shift) & 255) -
                                   static_cast<int>((expected >> shift) & 255);
            if (difference < -2 || difference > 2) return false;
        }
        return true;
    }

    HRESULT sample_center(IDirect3DDevice9* device, DWORD& pixel)
    {
        IDirect3DSurface9* target = nullptr;
        HRESULT hr = device->GetRenderTarget(0, &target);
        if (FAILED(hr)) return hr;
        D3DSURFACE_DESC desc = {};
        hr = target->GetDesc(&desc);
        IDirect3DSurface9* copy = nullptr;
        if (SUCCEEDED(hr) && desc.Format != D3DFMT_A8R8G8B8 && desc.Format != D3DFMT_X8R8G8B8)
            hr = D3DERR_INVALIDCALL;
        if (SUCCEEDED(hr)) hr = device->CreateOffscreenPlainSurface(desc.Width, desc.Height,
            desc.Format, D3DPOOL_SYSTEMMEM, &copy, nullptr);
        if (SUCCEEDED(hr)) hr = device->GetRenderTargetData(target, copy);
        D3DLOCKED_RECT rect = {};
        if (SUCCEEDED(hr))
        {
            hr = copy->LockRect(&rect, nullptr, D3DLOCK_READONLY);
            if (SUCCEEDED(hr))
            {
                const auto* row = static_cast<const uint8_t*>(rect.pBits) +
                                  static_cast<size_t>(desc.Height / 2) * static_cast<size_t>(rect.Pitch);
                std::memcpy(&pixel, row + static_cast<size_t>(desc.Width / 2) * 4, sizeof(pixel));
                copy->UnlockRect();
            }
        }
        if (copy) copy->Release();
        target->Release();
        return hr;
    }

    struct Vertex { float x, y, z, rhw, u, v; };
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR arguments, int)
{
    const bool expect_replacement = std::wstring(arguments).find(L"replacement") != std::wstring::npos;
    wchar_t module_path[32768] = {};
    const DWORD length = GetModuleFileNameW(nullptr, module_path, static_cast<DWORD>(std::size(module_path)));
    if (length == 0 || length >= std::size(module_path)) return 1;
    const auto toolkit_path = std::filesystem::path(module_path).parent_path() / L"TextureToolkit-x86.asi";
    // Called only by check-directx-textures.ps1 after exact upstream size + SHA-256 verification.
    const HMODULE toolkit = LoadLibraryW(toolkit_path.c_str());
    const bool reshade = GetModuleHandleW(L"d3d9.dll") != nullptr &&
                         GetProcAddress(GetModuleHandleW(L"d3d9.dll"), "ReShadeRegisterAddon") != nullptr;
    if (!toolkit) { result("failed-toolkit-load", false, reshade, 0, HRESULT_FROM_WIN32(GetLastError())); return 1; }

    WNDCLASSW definition = {};
    definition.lpfnWndProc = window_proc;
    definition.hInstance = instance;
    definition.lpszClassName = L"GamePanelDirectXTextureFixture";
    if (!RegisterClassW(&definition)) { result("failed-window-class", true, reshade, 0, E_FAIL); return 1; }
    HWND window = CreateWindowW(definition.lpszClassName, L"GamePanel synthetic texture test",
        WS_OVERLAPPEDWINDOW, 100, 100, 400, 300, nullptr, nullptr, instance, nullptr);
    if (!window) { result("failed-window", true, reshade, 0, E_FAIL); return 1; }
    ShowWindow(window, SW_SHOW);
    IDirect3D9* api = Direct3DCreate9(D3D_SDK_VERSION);
    if (!api) { result("unverified-no-d3d9-adapter", true, reshade, 0, E_FAIL); return 77; }
    D3DPRESENT_PARAMETERS presentation = {};
    presentation.Windowed = TRUE;
    presentation.SwapEffect = D3DSWAPEFFECT_COPY;
    presentation.BackBufferWidth = width;
    presentation.BackBufferHeight = height;
    presentation.BackBufferFormat = D3DFMT_X8R8G8B8;
    presentation.hDeviceWindow = window;
    presentation.PresentationInterval = D3DPRESENT_INTERVAL_IMMEDIATE;
    IDirect3DDevice9* device = nullptr;
    HRESULT hr = api->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, window,
        D3DCREATE_SOFTWARE_VERTEXPROCESSING, &presentation, &device);
    if (FAILED(hr)) { api->Release(); result("unverified-no-d3d9-adapter", true, reshade, 0, hr); return 77; }
    IDirect3DTexture9* texture = nullptr;
    hr = device->CreateTexture(texture_size, texture_size, 1, 0, D3DFMT_A8R8G8B8,
                               D3DPOOL_MANAGED, &texture, nullptr);
    D3DLOCKED_RECT pixels = {};
    if (SUCCEEDED(hr)) hr = texture->LockRect(0, &pixels, nullptr, 0);
    if (SUCCEEDED(hr))
    {
        for (UINT y = 0; y < texture_size; ++y)
        {
            auto* row = reinterpret_cast<DWORD*>(static_cast<uint8_t*>(pixels.pBits) +
                        static_cast<size_t>(y) * static_cast<size_t>(pixels.Pitch));
            for (UINT x = 0; x < texture_size; ++x) row[x] = original_color;
        }
        hr = texture->UnlockRect(0);
    }
    DWORD sampled = 0;
    MSG message = {};
    const Vertex quad[] = {
        { -0.5f, -0.5f, 0.0f, 1.0f, 0.0f, 0.0f },
        { static_cast<float>(width) - 0.5f, -0.5f, 0.0f, 1.0f, 1.0f, 0.0f },
        { -0.5f, static_cast<float>(height) - 0.5f, 0.0f, 1.0f, 0.0f, 1.0f },
        { static_cast<float>(width) - 0.5f, static_cast<float>(height) - 0.5f, 0.0f, 1.0f, 1.0f, 1.0f }
    };
    for (int frame = 0; SUCCEEDED(hr) && frame < 210; ++frame)
    {
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
        if (message.message == WM_QUIT) { hr = E_ABORT; break; }
        hr = device->Clear(0, nullptr, D3DCLEAR_TARGET, D3DCOLOR_XRGB(0, 0, 0), 1.0f, 0);
        if (FAILED(hr)) break;
        hr = device->BeginScene();
        if (FAILED(hr)) break;
        device->SetRenderState(D3DRS_LIGHTING, FALSE);
        device->SetRenderState(D3DRS_ZENABLE, FALSE);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, FALSE);
        device->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
        device->SetFVF(D3DFVF_XYZRHW | D3DFVF_TEX1);
        device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
        device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_TEXTURE);
        device->SetTextureStageState(1, D3DTSS_COLOROP, D3DTOP_DISABLE);
        device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_POINT);
        device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
        device->SetTexture(0, texture);
        hr = device->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, quad, sizeof(Vertex));
        const HRESULT end = device->EndScene();
        if (SUCCEEDED(hr)) hr = end;
        if (SUCCEEDED(hr) && frame == 200) hr = sample_center(device, sampled);
        if (SUCCEEDED(hr)) hr = device->Present(nullptr, nullptr, nullptr, nullptr);
        Sleep(16);
    }
    const bool matches = SUCCEEDED(hr) && close_channel(sampled,
        expect_replacement ? replacement_color : original_color);
    result(matches ? "verified-rendered-pixel" : "failed-rendered-pixel", true, reshade, sampled, hr);
    if (texture) texture->Release();
    device->Release(); api->Release(); DestroyWindow(window);
    // Leave the pinned module loaded until process exit; upstream must not be unloaded under its workers.
    return matches ? 0 : 1;
}
