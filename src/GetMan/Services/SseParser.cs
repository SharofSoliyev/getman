using System.Text;

namespace GetMan.Services;

/// <summary>
/// The line-by-line half of the server-sent events format, kept away from the socket so it can be
/// tested without one. Follows the WHATWG event stream rules: comments start with a colon, a field
/// is <c>name: value</c> with one optional space eaten after the colon, <c>data</c> lines join with
/// a newline, and a blank line dispatches whatever has accumulated.
/// </summary>
public sealed class SseParser
{
    private readonly StringBuilder _data = new();
    private string _eventName;
    private string _id;

    /// <summary>The last id the stream sent, which a reconnect would send back as Last-Event-ID.</summary>
    public string LastEventId { get; private set; }

    /// <summary>The reconnection delay the server asked for, if it asked.</summary>
    public int? RetryMs { get; private set; }

    /// <summary>Returns a message when the line completed one, otherwise null.</summary>
    public StreamMessage Feed(string line)
    {
        if (line == null) return null;

        // A blank line ends the event. An event with no data is a keep-alive, not a message.
        if (line.Length == 0)
        {
            if (_data.Length == 0)
            {
                _eventName = null;
                return null;
            }

            // The spec strips exactly one trailing newline, the one the last data line added.
            if (_data[^1] == '\n') _data.Length--;

            var message = new StreamMessage
            {
                Direction = StreamDirection.In,
                Text = _data.ToString(),
                EventName = string.IsNullOrEmpty(_eventName) ? "message" : _eventName,
                EventId = _id,
                SizeBytes = Encoding.UTF8.GetByteCount(_data.ToString())
            };

            _data.Clear();
            _eventName = null;
            _id = null;
            return message;
        }

        // A line that starts with a colon is a comment. Servers send these to hold the connection open.
        if (line[0] == ':') return null;

        var colon = line.IndexOf(':');
        string field, value;
        if (colon < 0)
        {
            field = line;
            value = string.Empty;
        }
        else
        {
            field = line[..colon];
            value = line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
        }

        switch (field)
        {
            case "event":
                _eventName = value;
                break;

            case "data":
                _data.Append(value).Append('\n');
                break;

            case "id":
                // An id containing a null byte is ignored rather than stored.
                if (!value.Contains('\0'))
                {
                    _id = value;
                    LastEventId = value;
                }
                break;

            case "retry":
                if (value.Length > 0 && value.All(char.IsAsciiDigit) && int.TryParse(value, out var retry))
                    RetryMs = retry;
                break;

            // Any other field name is ignored, which is what lets the format grow.
        }

        return null;
    }
}
