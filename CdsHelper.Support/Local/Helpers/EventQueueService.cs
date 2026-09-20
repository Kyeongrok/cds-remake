using System.Collections.ObjectModel;

namespace CdsHelper.Support.Local.Helpers;

public enum EventType
{
    Info,
    Warning,
    Error,
    MigrationSkipped,
    DataLoaded,
    DataSaved
}

public class AppEvent
{
    public DateTime Timestamp { get; set; }
    public EventType Type { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string TypeDisplay => Type switch
    {
        EventType.Info => "정보",
        EventType.Warning => "경고",
        EventType.Error => "오류",
        EventType.MigrationSkipped => "마이그레이션 스킵",
        EventType.DataLoaded => "데이터 로드",
        EventType.DataSaved => "데이터 저장",
        _ => Type.ToString()
    };

    public string TimestampDisplay => Timestamp.ToString("HH:mm:ss.fff");
}

public class EventQueueService
{
    private static readonly Lazy<EventQueueService> _instance = new(() => new EventQueueService());
    public static EventQueueService Instance => _instance.Value;

    private readonly List<AppEvent> _events = new();
    private readonly object _lock = new();

    public event EventHandler? EventAdded;

    private EventQueueService() { }

    public void Add(EventType type, string source, string message)
    {
        lock (_lock)
        {
            _events.Add(new AppEvent
            {
                Timestamp = DateTime.Now,
                Type = type,
                Source = source,
                Message = message
            });
        }

        EventAdded?.Invoke(this, EventArgs.Empty);
    }

    public void Info(string source, string message) => Add(EventType.Info, source, message);
    public void Warning(string source, string message) => Add(EventType.Warning, source, message);
    public void Error(string source, string message) => Add(EventType.Error, source, message);
    public void MigrationSkipped(string source, string message) => Add(EventType.MigrationSkipped, source, message);
    public void DataLoaded(string source, string message) => Add(EventType.DataLoaded, source, message);
    public void DataSaved(string source, string message) => Add(EventType.DataSaved, source, message);

    public IReadOnlyList<AppEvent> GetAll()
    {
        lock (_lock)
        {
            return _events.ToList();
        }
    }

    public IReadOnlyList<AppEvent> GetByType(EventType type)
    {
        lock (_lock)
        {
            return _events.Where(e => e.Type == type).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }
}
