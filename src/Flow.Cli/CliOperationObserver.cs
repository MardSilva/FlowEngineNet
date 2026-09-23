namespace Flow.Cli;

internal interface ICliOperationObserver
{
    public void FileWritten(string path);
}
