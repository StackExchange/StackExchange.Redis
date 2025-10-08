# RespCommandGenerator

Emit basic RESP command bodies.

The purpose of this generator is to interpret inputs like:

``` c#
[RespCommand] // optional: include explicit command text
public int void Foo(string key, int delta, double x);
```

and implement the relevant sync and async core logic, including
implementing a custom `IRespFormatter<(string, int, double)>`. Note that 
the formatter can be reused between commands, so the names are not used internally.

Note that parameters named `key` are detected automatically for sharding purposes;
when this is not suitable,`[Key]` can be used instead to denote a parameter to use
for sharding - for example `partial void Rename([Key] string fromKey, string toKey)`.