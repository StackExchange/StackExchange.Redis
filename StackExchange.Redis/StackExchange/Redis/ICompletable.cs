using System.Text;

namespace StackExchange.Redis
{
    interface ICompletable
    {
        bool TryComplete(bool isAsync);
        void AppendStormLog(StringBuilder sb);
    }
}
