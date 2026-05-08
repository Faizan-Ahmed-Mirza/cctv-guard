using CCTV_Guard.Models.DTOs.User;
using CCTV_Guard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCTV_Guard.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService) => _userService = userService;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] string? search) =>
        Ok(await _userService.GetAllAsync(role, status, search));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _userService.GetByIdAsync(id);
        return user == null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        var (user, error) = await _userService.CreateAsync(req);
        if (error != null) return BadRequest(new { message = error });
        return CreatedAtAction(nameof(GetById), new { id = user!.Id }, user);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest req)
    {
        var (user, error) = await _userService.UpdateAsync(id, req);
        if (error == "User not found.") return NotFound();
        if (error != null) return BadRequest(new { message = error });
        return Ok(user);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> PatchStatus(Guid id, [FromBody] PatchStatusRequest req)
    {
        var (ok, error) = await _userService.PatchStatusAsync(id, req.Status);
        if (!ok) return error == "User not found." ? NotFound() : BadRequest(new { message = error });
        return Ok(await _userService.GetByIdAsync(id));
    }

    [HttpPatch("{id:guid}/role")]
    public async Task<IActionResult> PatchRole(Guid id, [FromBody] PatchRoleRequest req)
    {
        var (ok, error) = await _userService.PatchRoleAsync(id, req.Role);
        if (!ok) return error == "User not found." ? NotFound() : BadRequest(new { message = error });
        return Ok(await _userService.GetByIdAsync(id));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var (ok, error) = await _userService.DeleteAsync(id);
        if (!ok) return error == "User not found." ? NotFound() : BadRequest(new { message = error });
        return NoContent();
    }
}
