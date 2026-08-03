namespace Content.Server.MoMMI
{
    public interface IMoMMILink
    {
        void SendOOCMessage(string sender, string message);
        void SendDeadChatMessage(string sender, string message);
    }
}
