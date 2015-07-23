## *Quick links*
> - [Inversion of control components](#ioc)
> - [Redis command handlers](#handlers)

# StackExchange.Redis inversion of control components <a name="ioc"></a>

*StackExchange.Redis* provides extensibility points to allow third-party developers to implement execution pipeline customizations.

Basically this is done by using `RedisServiceFactory` class. For example:

	RedisServiceFactory.Register<ISomeService, SomeServiceImplementation>();

If you try to provide an implementation to an unsupported service, `RedisServiceFactory.Register<TService, TImpl>()` will throw a `System.ArgumentException`. 

Thus, you should only register implementations for already supported *StackExchange.Redis* service (for example, an [`IRedisCommandHandler`](#handlers) implementation).

Service implementations can be also unregistered:

	RedisServiceFactory.Unregister<ISomeService, SomeServiceImplementation>();


### Handling service implementation object life-cycle

When execution pipeline gets a set of some service implementations, these implementations are instances created everytime they're needed. This may work in some scenarios while in others implementation instances life-cycle should be handled in other ways.

Implementation instance life-cycle can be customized by setting `RedisServiceFactory.LifeTimeHandler` property:

        // For example, this code may be implemented before executing any
        // Redis command (i.e. during ConnectionMultiplexer configuration!)

        // This implementation instance life-cycle handler will create 
        // singletons, since it will create each implementation instance per
        // entire host application life-cycle.
        HashSet<Type> implTypes = new HashSet<Type>();
        Dictionary<Type, object> implInstances = new Dictionary<Type, object>();

        RedisServiceFactory.LifeTimeHandler = (serviceType, implType) =>
        {
            object implInstance;

            if (implTypes.Add(implType))
            {
                implInstance = Activator.CreateInstance(implType);
                implInstances.Add(implType, implInstance);
            }
            else
                implInstance = implInstances[implType];

            return implInstance;
        };

## Redis command handlers <a name="handlers"></a>

Redis command execution can be intercepted by defining *redis command handlers*, which are implementations of `IRedisCommandHandler` interface.

A *Redis command handler* can intercept a command when is about to be executed and when its execution has been already finished.

For example, a sample implementation may look like as follows:

	public class SampleRedisCommandHandler : IRedisCommandHandler
	{
		public void OnExecuting(RedisCommand command, RedisKey[] involvedKeys = null, RedisValue[] involvedValues = null)
		{
			// Do stuff here before the command gets executed
		}

		public void OnExecuted(RedisCommand command,  ref object result, RedisKey[] involvedKeys = null)
		{
			// Do stuff here after the command has been executed
		}
	}

A *Redis command handler* can both add behavior and modify what's going to be written when the command is transmitted to the Redis server and also modify what's going to be returned once execution pipeline has ended.

For example, `OnExecuting` can convert to lower any string being written by a `SET` command:

	public void OnExecuting(RedisCommand command, RedisKey[] involvedKeys = null, RedisValue[] involvedValues = null)
	{
		if(command === RedisCommand.SET)
		{
			// The value of SET command is the value to be written to a given
			// Redis key...
			involvedValues[0] = ((string)involvedValues[0]).ToLowerInvariant();
		}
	}

Furthermore, `SET` result can be rewritten:

	public void OnExecuted<TResult>(RedisCommand command,  ref object result, RedisKey[] involvedKeys = null)
	{
		// You should try to preserve source type. For example, this casts the
		// string back to RedisValue.
		// Now all SET results are lowercase!
		result = (RedisValue)((string)result).ToLowerInvarant();
	}

### Activating handlers for specific Redis commands.

In order to optimize Redis command execution pipeline, command handlers can be activated for a subset of commands.

The following sample code should be run before any Redis command gets executed (i.e. at the same time the `ConnectionMultiplexer` is created):

	ConfigurationOptions options = new ConfigurationOptions();
	// other configuration options like endpoints and so on...
	options.CommandHandler.ActivateForCommands(RedisCommand.SET, RedisCommand.HMSET);
	
	ConnectionMultiplexer multiplexer = ConnectionMultiplexer.Connect(options);