using System.Drawing;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ScreenForge.Record;

internal sealed class DxgiFrameSource : IFrameSource
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly ID3D11Texture2D _staging;
    private readonly Rectangle _crop;
    private readonly int _width;
    private readonly int _height;
    private bool _released = true;
    private bool _disposed;

    private DxgiFrameSource(
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDXGIOutputDuplication duplication,
        ID3D11Texture2D staging,
        Rectangle crop,
        int width,
        int height)
    {
        _device = device;
        _context = context;
        _duplication = duplication;
        _staging = staging;
        _crop = crop;
        _width = width;
        _height = height;
    }

    public static DxgiFrameSource? TryCreate(Rectangle pixelRegion)
    {
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1 adapter).Success; a++)
            {
                using (adapter)
                {
                    for (uint o = 0; adapter.EnumOutputs(o, out IDXGIOutput output).Success; o++)
                    {
                        using (output)
                        {
                            var desc = output.Description;
                            var desk = desc.DesktopCoordinates;
                            var outputRect = Rectangle.FromLTRB(desk.Left, desk.Top, desk.Right, desk.Bottom);
                            if (!outputRect.Contains(pixelRegion))
                                continue;

                            var crop = new Rectangle(
                                pixelRegion.X - outputRect.X,
                                pixelRegion.Y - outputRect.Y,
                                pixelRegion.Width,
                                pixelRegion.Height);

                            Result hr = D3D11.D3D11CreateDevice(
                                adapter,
                                DriverType.Unknown,
                                DeviceCreationFlags.BgraSupport,
                                [FeatureLevel.Level_11_0, FeatureLevel.Level_10_0],
                                out ID3D11Device device,
                                out ID3D11DeviceContext context);
                            if (hr.Failure || device is null || context is null)
                                continue;

                            try
                            {
                                using var output1 = output.QueryInterface<IDXGIOutput1>();
                                var dupl = output1.DuplicateOutput(device);
                                var stagingDesc = new Texture2DDescription
                                {
                                    Width = (uint)pixelRegion.Width,
                                    Height = (uint)pixelRegion.Height,
                                    MipLevels = 1,
                                    ArraySize = 1,
                                    Format = Format.B8G8R8A8_UNorm,
                                    SampleDescription = new SampleDescription(1, 0),
                                    Usage = ResourceUsage.Staging,
                                    CPUAccessFlags = CpuAccessFlags.Read,
                                    BindFlags = BindFlags.None,
                                };
                                var staging = device.CreateTexture2D(stagingDesc);
                                return new DxgiFrameSource(device, context, dupl, staging, crop, pixelRegion.Width, pixelRegion.Height);
                            }
                            catch
                            {
                                context.Dispose();
                                device.Dispose();
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // DXGI duplication unavailable (session lock, another capturer, VM).
        }

        return null;
    }

    public bool TryCopyBgra(Span<byte> dest, out long qpcHundredNanos)
    {
        qpcHundredNanos = 0;
        if (_disposed || dest.Length < _width * _height * 4)
            return false;

        if (!_released)
        {
            try { _duplication.ReleaseFrame(); } catch { /* already released */ }
            _released = true;
        }

        Result hr = _duplication.AcquireNextFrame(40, out OutduplFrameInfo info, out IDXGIResource? resource);
        if (hr.Failure || resource is null)
            return false;

        _released = false;
        try
        {
            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            var box = new Box(
                _crop.X,
                _crop.Y,
                0,
                _crop.X + _crop.Width,
                _crop.Y + _crop.Height,
                1);
            _context.CopySubresourceRegion(_staging, 0, 0, 0, 0, texture, 0, box);
            MappedSubresource mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                int srcStride = (int)mapped.RowPitch;
                int dstStride = _width * 4;
                unsafe
                {
                    byte* src = (byte*)mapped.DataPointer;
                    for (int y = 0; y < _height; y++)
                    {
                        new Span<byte>(src + y * srcStride, dstStride).CopyTo(dest.Slice(y * dstStride, dstStride));
                    }
                }
            }
            finally
            {
                _context.Unmap(_staging, 0);
            }

            qpcHundredNanos = info.LastPresentTime;
            if (qpcHundredNanos <= 0)
                qpcHundredNanos = info.LastMouseUpdateTime;
            return true;
        }
        finally
        {
            resource.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_released)
        {
            try { _duplication.ReleaseFrame(); } catch { /* ignore */ }
        }
        _duplication.Dispose();
        _staging.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
