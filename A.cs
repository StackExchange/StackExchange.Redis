public interface IArgs { void Map(); }
public struct S : IArgs { public int Arg0; public long Arg1; public void Map() { } }
public interface IInner { int Do(int a, long b); }

// candidate delegate: like Func<TState,TIn,TResult> but with 'in' on the state
public delegate TResult ArgsFunc<TState, TIn, TResult>(in TState state, TIn inner) where TState : struct, IArgs;

public class C
{
    private TResult Execute<TState, TResult>(in TState state, ArgsFunc<TState, IInner, TResult> op)
        where TState : struct, IArgs => op(in state, null!);

    // TEST 1: fully implicit lambda (what the generator emits today)
    public int Implicit() => Execute(new S(), static (state, inner) => inner.Do(state.Arg0, state.Arg1));
}
