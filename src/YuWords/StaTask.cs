namespace YuWords;

/// <summary>COM（Outlook・IME）を使う処理を、UI を止めずに専用の STA スレッドで動かす</summary>
static class StaTask
{
    public static Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception e) { tcs.SetException(e); }
        })
        { IsBackground = true, Name = "YuWords worker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
