using System.Text;

namespace StackExchange.Redis
{
    internal interface ICompletable
    {
        void AppendStormLog(StringBuilder sb);

        bool TryComplete(bool isAsync);
    }
}
