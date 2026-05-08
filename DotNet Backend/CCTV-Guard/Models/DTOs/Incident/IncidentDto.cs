namespace CCTV_Guard.Models.DTOs.Incident;

public class IncidentDto
{
    public string Id { get; set; } = string.Empty;
    public string CameraId { get; set; } = string.Empty;
    public string CameraName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ThumbnailUrl { get; set; }
    public BoundingBoxDto? BoundingBox { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class BoundingBoxDto
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class IncidentFilterParams
{
    public string? Type { get; set; }
    public string? Severity { get; set; }
    public string? Status { get; set; }
    public string? CameraId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class PagedResult<T>
{
    public List<T> Data { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ResolveIncidentRequest
{
    public string? Notes { get; set; }
}
