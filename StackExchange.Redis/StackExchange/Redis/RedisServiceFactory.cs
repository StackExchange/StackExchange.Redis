namespace StackExchange.Redis
{
    using System;
    using System.Linq;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a basic inversion of control container of Redis-specific services used across StackExchange.Redis infrastructure.
    /// </summary>
    /// <remarks>
    /// This service factory creates instances of service implementation using regular reflection (<see cref="System.Reflection.Activator" />). That is, whenever
    /// a caller requests service implementations, new instance will be created. This is a default behavior and it might not work as good as it sounds in some scenarios
    /// like multi-threaded environments (i.e. ASP.NET WebAPI with IIS or OWIN). 
    /// 
    /// <code>LifeTimeHandler</code> property provides an extensibility point where you may plug a delegate that receives both service and implementation types whenever this factory
    /// needs to obtain service implementation instances. For example, setting a LifeTimeHandler can force this factory to retain per-OWIN context instances until an HTTP request ends.
    ///
    /// The so-called <code>LifeTimeHandler</code> must be set before registering and obtaining service implementations.
    /// 
    /// Also, service implementation registrations must be done before calling <see cref="GetImplementations"/> method or this factory will not
    /// take any service implementation registration in account.
    /// </remarks>
    public static class RedisServiceFactory
    {
        private readonly static HashSet<Type> supportedServices = new HashSet<Type> { typeof(IRedisCommandHandler) };
        private readonly static Dictionary<Type, HashSet<Type>> services = new Dictionary<Type, HashSet<Type>>();

        private readonly static Lazy<IEnumerable<IRedisCommandHandler>> lazyCommandHandlers = new Lazy<IEnumerable<IRedisCommandHandler>>(() => GetImplementations<IRedisCommandHandler>());

        internal static HashSet<Type> SupportedService { get { return supportedServices; } }

        private static Dictionary<Type, HashSet<Type>> Services { get { return services; } }

        /// <summary>
        /// Gets or sets a delegate which implements how service implementations are obtained.
        /// </summary>
        public static Func<Type, Type, object> LifeTimeHandler { get; set; }

        /// <summary>
        /// Registers an implementation for the given service. Calling this method more than once will result in a many configured implementations for the same service.
        /// </summary>
        /// <typeparam name="TService">The type of the service</typeparam>
        /// <typeparam name="TImpl">The type of service implementation</typeparam>
        /// <returns>True if implementation could be successfully registered. False, if given implementation was already registered before</returns>
        public static bool Register<TService, TImpl>()
            where TImpl : class, TService, new()
        {
            if (!supportedServices.Contains(typeof(TService)))
            {
                throw new ArgumentException("Cannot register an implementation of an unsupported service type");
            }

            if (!Services.ContainsKey(typeof(TService)))
            {
                HashSet<Type> implementations = new HashSet<Type>();
                Services[typeof(TService)] = implementations;

                return implementations.Add(typeof(TImpl));
            }
            else
            {
                return Services[typeof(TService)].Add(typeof(TImpl));
            }
        }

        /// <summary>
        /// Unregisters an implementation from some given service.
        /// </summary>
        /// <typeparam name="TService">The type of service</typeparam>
        /// <typeparam name="TImpl">The type of service implementation</typeparam>
        /// <returns>True if given implementation could be unregistered. False if it couldn't be unregister (maybe it wasn't registered at all...)</returns>
        public static bool Unregister<TService, TImpl>()
            where TImpl : class, TService, new()
        {
            if (!supportedServices.Contains(typeof(TService)))
            {
                throw new ArgumentException("Cannot unregister an implementation of an unsupported service type");
            }

            return Services.ContainsKey(typeof(TService)) && Services[typeof(TService)].Remove(typeof(TImpl));
        }

        /// <summary>
        /// Gets all service implementations
        /// </summary>
        /// <typeparam name="TService">The type of the service for which this operation needs to obtain all its implementations</typeparam>
        /// <returns>Service implementations</returns>
        internal static IEnumerable<TService> GetImplementations<TService>()
            where TService : class
        {
            if (Services.ContainsKey(typeof(TService)))
            {
                return Services[typeof(TService)].Select
                (
                    implType =>
                    {
                        if (LifeTimeHandler == null)
                            return (TService)Activator.CreateInstance(implType);
                        else return (TService)LifeTimeHandler(typeof(TService), implType);
                    }
                ).ToList();
            }
            else return null;
        }

        /// <summary>
        /// Gets all command handler implementations. If there is no implementation, it returns null.
        /// </summary>
        internal static IEnumerable<IRedisCommandHandler> CommandHandlers
        {
            get
            {
                return lazyCommandHandlers.Value;
            }
        }
    }
}