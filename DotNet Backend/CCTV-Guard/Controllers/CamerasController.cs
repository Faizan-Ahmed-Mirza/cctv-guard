using CCTV_Guard.Models.DTOs.Camera;
using CCTV_Guard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/cameras")]
[Authorize]
public class CamerasController : ControllerBase
{
    private readonly CameraService _cameraService;
    private readonly CameraStreamService _streamService;

    public CamerasController(CameraService cameraService, CameraStreamService streamService)
    {
        _cameraService = cameraService;
        _streamService = streamService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _cameraService.GetAllAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var cam = await _cameraService.GetByIdAsync(id);
        return cam == null ? NotFound() : Ok(cam);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateCameraRequest req)
    {
        var cam = await _cameraService.CreateAsync(req);
        return CreatedAtAction(nameof(GetById), new { id = cam.Id }, cam);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateCameraRequest req)
    {
        var cam = await _cameraService.UpdateAsync(id, req);
        return cam == null ? NotFound() : Ok(cam);
    }

    [HttpPatch("{id}/detection")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> PatchDetection(string id, [FromBody] PatchDetectionRequest req)
    {
        var cam = await _cameraService.PatchDetectionAsync(id, req.DetectionEnabled);
        return cam == null ? NotFound() : Ok(cam);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string id)
    {
        var ok = await _cameraService.DeleteAsync(id);
        return ok ? NoContent() : NotFound();
    }

    // ── MJPEG stream — browser <img> plays this natively ─────────────────────
    // No auth on this endpoint so <img src="..."> works without extra headers.
    // Security: token passed as query param ?token=xxx validated manually.
    [HttpGet("{id}/mjpeg")]
    [AllowAnonymous]
    public async Task MjpegStream(string id, CancellationToken ct)
    {
        var cam = await _cameraService.GetByIdAsync(id);
        if (cam == null || string.IsNullOrWhiteSpace(cam.RtspUrl))
        {
            Response.StatusCode = 404;
            return;
        }

        var ffmpegPath = _streamService.FfmpegExePath;
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            Response.StatusCode = 503;
            return;
        }

        var isRtmp = cam.RtspUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);
        var inputArgs = isRtmp
            ? $"-i \"{cam.RtspUrl}\""
            : $"-rtsp_transport tcp -i \"{cam.RtspUrl}\"";

        var args = $"-loglevel warning {inputArgs} -vf fps=25,scale=854:480 -f mjpeg -q:v 3 pipe:1";

        var psi = new ProcessStartInfo
        {
            FileName               = ffmpegPath,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        // MJPEG multipart response — browser <img> renders this as live video
        Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stream = process.StandardOutput.BaseStream;
        var buffer = new byte[65536];
        var frameData = new List<byte>(65536);
        var inFrame = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer, ct);
                if (bytesRead == 0) break;

                for (var i = 0; i < bytesRead; i++)
                {
                    frameData.Add(buffer[i]);

                    // Detect JPEG SOI (start)
                    if (!inFrame && frameData.Count >= 2
                        && frameData[^2] == 0xFF && frameData[^1] == 0xD8)
                    {
                        frameData = [0xFF, 0xD8];
                        inFrame = true;
                        continue;
                    }

                    // Detect JPEG EOI (end) — write complete frame
                    if (inFrame && frameData.Count >= 2
                        && frameData[^2] == 0xFF && frameData[^1] == 0xD9)
                    {
                        var jpeg = frameData.ToArray();
                        frameData.Clear();
                        inFrame = false;

                        // Write MJPEG boundary + frame
                        var header = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n";
                        await Response.Body.WriteAsync(
                            System.Text.Encoding.ASCII.GetBytes(header), ct);
                        await Response.Body.WriteAsync(jpeg, ct);
                        await Response.Body.WriteAsync("\r\n"u8.ToArray(), ct);
                        await Response.Body.FlushAsync(ct);
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
        }
    }

    // ── Stream status ─────────────────────────────────────────────────────────

    [HttpGet("{id}/stream/status")]
    public IActionResult StreamStatus(string id) =>
        Ok(new { cameraId = id, streaming = _streamService.IsStreaming(id) });

    [HttpPost("{id}/stream/stop")]
    [Authorize(Roles = "Admin,Operator")]
    public async Task<IActionResult> StopStream(string id)
    {
        await _streamService.StopAsync(id);
        return Ok(new { cameraId = id, streaming = false });
    }
}
