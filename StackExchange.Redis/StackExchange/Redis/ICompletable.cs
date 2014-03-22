using System.Text;

namespace StackExchange.Redis
{
    interface ICompletable
    {
        bool TryComplete(bool isAsync, bool allowSyncContinuations);
        void AppendStormLog(StringBuilder sb);
    }
}
