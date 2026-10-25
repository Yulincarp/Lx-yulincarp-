namespace Lx.Probe.Diagnostics;

// 同一 Windows 会话只允许一个启动器实例。
// 第二个实例通过命名事件通知第一个实例"把窗口叫到前面"，然后自己退出。
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _eventName;
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activateEvent;

    public SingleInstanceGuard(string name)
    {
        _eventName = name + ".Activate";
        _mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        IsFirstInstance = createdNew;
        if (createdNew)
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
        }
    }

    public bool IsFirstInstance { get; }

    // 第二个实例调用：叫醒第一个实例。第一个实例还是旧版/没建事件时返回 false。
    public bool SignalExisting()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(_eventName);
            handle.Set();
            return true;
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // 第一个实例的后台线程调用：最多等 timeout，收到通知返回 true。
    public bool WaitForActivation(TimeSpan timeout)
    {
        if (_activateEvent is null)
        {
            return false;
        }

        try
        {
            return _activateEvent.WaitOne(timeout);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 不是当前线程持有（理论上不会发生），忽略
            }
        }

        _mutex.Dispose();
        _activateEvent?.Dispose();
    }
}
