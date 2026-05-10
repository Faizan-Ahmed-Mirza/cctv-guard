namespace CCTV_Guard.Models.Entities;

/// <summary>
/// Stores a FaceNet 128-dimension embedding vector for a registered person.
/// The embedding is serialised as a JSON array of floats and stored in SQL Server.
/// On AI service startup, all embeddings are synced from here to the Python process.
/// This ensures face registrations survive AI service restarts and are backed up in the DB.
/// </summary>
public class FacialEmbedding
{
    public int    Id           { get; set; }
    public string Username     { get; set; } = string.Empty;  // matches User.Username
    public string EmbeddingJson { get; set; } = string.Empty; // JSON array of 128 floats
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public string RegisteredBy { get; set; } = string.Empty;  // username of admin who registered
}
