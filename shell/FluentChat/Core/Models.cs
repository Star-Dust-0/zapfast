using System;
using System.Text.Json.Serialization;

namespace FluentChat.Core;

/// <summary>Wire models for the Fluent Chat core protocol (v1).
/// See docs/fluent-chat-core-protocol.md.</summary>
public sealed class LinkState
{
    [JsonPropertyName("state")] public string State { get; set; } = "starting";
    [JsonPropertyName("qr")] public string? Qr { get; set; }
    [JsonPropertyName("pair_code")] public string? PairCode { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public sealed class Me
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("lid")] public string? Lid { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("about")] public string? About { get; set; }
}

public sealed class LastMessage
{
    [JsonPropertyName("from_me")] public bool FromMe { get; set; }
    [JsonPropertyName("sender")] public string? Sender { get; set; }
    [JsonPropertyName("sender_name")] public string? SenderName { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

public sealed class Chat
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "direct";
    [JsonPropertyName("last_activity")] public long LastActivity { get; set; }
    [JsonPropertyName("unread")] public uint Unread { get; set; }
    [JsonPropertyName("marked_unread")] public bool MarkedUnread { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
    [JsonPropertyName("pinned")] public bool Pinned { get; set; }
    [JsonPropertyName("pinned_at")] public long PinnedAt { get; set; }
    [JsonPropertyName("muted_until")] public long? MutedUntil { get; set; }
    [JsonPropertyName("last")] public LastMessage? Last { get; set; }
    [JsonPropertyName("favorite")] public bool Favorite { get; set; }
    [JsonPropertyName("read_only")] public bool ReadOnly { get; set; }
    [JsonPropertyName("left")] public bool Left { get; set; }
    [JsonPropertyName("locked")] public bool Locked { get; set; }
    [JsonPropertyName("ephemeral_expiration")] public uint? EphemeralExpiration { get; set; }
    [JsonPropertyName("labels")] public string[]? Labels { get; set; }
    [JsonPropertyName("participants")] public string[]? Participants { get; set; }
}

public sealed class Message
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("chat")] public string Chat { get; set; } = "";
    [JsonPropertyName("sender")] public string? Sender { get; set; }
    [JsonPropertyName("sender_name")] public string? SenderName { get; set; }
    [JsonPropertyName("from_me")] public bool FromMe { get; set; }
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
    [JsonPropertyName("content")] public JsonElement Content { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("quoted")] public JsonElement? Quoted { get; set; }
    [JsonPropertyName("edited")] public bool Edited { get; set; }
    [JsonPropertyName("forwarded")] public bool Forwarded { get; set; }

    public string ContentKind => Content.ValueKind == JsonValueKind.Object
        && Content.TryGetProperty("kind", out JsonElement kind)
        && kind.ValueKind == JsonValueKind.String
            ? kind.GetString() ?? "unsupported"
            : "unsupported";

    public string Text
    {
        get
        {
            if (Content.ValueKind != JsonValueKind.Object)
            {
                return "";
            }
            if (Content.TryGetProperty("text", out JsonElement text))
            {
                return text.GetString() ?? "";
            }
            if (Content.TryGetProperty("caption", out JsonElement caption))
            {
                return caption.GetString() ?? "";
            }
            return "[" + ContentKind + "]";
        }
    }
}

public sealed class MessagePage
{
    [JsonPropertyName("chat")] public string Chat { get; set; } = "";
    [JsonPropertyName("messages")] public Message[] Messages { get; set; } = Array.Empty<Message>();
    [JsonPropertyName("older")] public bool Older { get; set; }
    [JsonPropertyName("complete")] public bool Complete { get; set; }
}
