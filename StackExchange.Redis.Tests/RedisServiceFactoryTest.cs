using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public sealed class RedisServiceFactoryTest : TestBase
    {
        #region Support classes
        private interface ICustomService
        {
        }
        private class CustomServiceImplementation : ICustomService
        {
        }
        private class CustomServiceImplementation2 : ICustomService
        {
        }
        private class CustomServiceImplementation3 : ICustomService
        {
        }

        private interface ICustomService2
        {
        }
        private class CustomService2Implementation1 : ICustomService2
        {
        }
        private class CustomService2Implementation2 : ICustomService2
        {
        }

        private class CustomService2Implementation3 : ICustomService2
        {
        }

        private interface ICustomService3
        {
        }
        private class CustomService3Implementation1 : ICustomService3
        {
        }
        private class CustomService3Implementation2 : ICustomService3
        {
        }
        private class CustomService3Implementation3 : ICustomService3
        {
        }
        #endregion

        [Test]
        public void CanRegisterSomeServiceWithImplementations()
        {
            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation>();
            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation2>();
            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation3>();

            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation1>();
            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation2>();
            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation3>();


            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation1>();
            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation2>();
            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation3>();

            IEnumerable<ICustomService> impls1 = RedisServiceFactory.GetImplementations<ICustomService>();
            IEnumerable<ICustomService2> impls2 = RedisServiceFactory.GetImplementations<ICustomService2>();
            IEnumerable<ICustomService3> impls3 = RedisServiceFactory.GetImplementations<ICustomService3>();

            Assert.IsNotNull(impls1);
            Assert.IsNotNull(impls2);
            Assert.IsNotNull(impls3);
            Assert.AreEqual(3, impls1.Count());
            Assert.AreEqual(3, impls2.Count());
            Assert.AreEqual(3, impls3.Count());

            Assert.AreEqual(1, impls1.Count(impl => impl.GetType() == typeof(CustomServiceImplementation)));
            Assert.AreEqual(1, impls1.Count(impl => impl.GetType() == typeof(CustomServiceImplementation2)));
            Assert.AreEqual(1, impls1.Count(impl => impl.GetType() == typeof(CustomServiceImplementation3)));

            Assert.AreEqual(1, impls2.Count(impl => impl.GetType() == typeof(CustomService2Implementation1)));
            Assert.AreEqual(1, impls2.Count(impl => impl.GetType() == typeof(CustomService2Implementation2)));
            Assert.AreEqual(1, impls2.Count(impl => impl.GetType() == typeof(CustomService2Implementation3)));

            Assert.AreEqual(1, impls3.Count(impl => impl.GetType() == typeof(CustomService3Implementation1)));
            Assert.AreEqual(1, impls3.Count(impl => impl.GetType() == typeof(CustomService3Implementation2)));
            Assert.AreEqual(1, impls3.Count(impl => impl.GetType() == typeof(CustomService3Implementation3)));
        }

        [Test]
        public void CanUnregisterSomeServiceImplementations()
        {
            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation>();
            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation2>();
            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation3>();

            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation1>();
            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation2>();
            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation3>();

            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation1>();
            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation2>();
            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation3>();

            RedisServiceFactory.Unregister<ICustomService, CustomServiceImplementation2>();
            RedisServiceFactory.Unregister<ICustomService3, CustomService3Implementation3>();

            IEnumerable<ICustomService> impls1 = RedisServiceFactory.GetImplementations<ICustomService>();
            IEnumerable<ICustomService2> impls2 = RedisServiceFactory.GetImplementations<ICustomService2>();
            IEnumerable<ICustomService3> impls3 = RedisServiceFactory.GetImplementations<ICustomService3>();

            Assert.AreEqual(2, impls1.Count());
            Assert.AreEqual(3, impls2.Count());
            Assert.AreEqual(2, impls3.Count());

            Assert.AreEqual(1, impls1.Count(impl => impl.GetType() == typeof(CustomServiceImplementation)));
            Assert.AreEqual(1, impls1.Count(impl => impl.GetType() == typeof(CustomServiceImplementation3)));

            Assert.AreEqual(1, impls2.Count(impl => impl.GetType() == typeof(CustomService2Implementation1)));
            Assert.AreEqual(1, impls2.Count(impl => impl.GetType() == typeof(CustomService2Implementation2)));
            Assert.AreEqual(1, impls2.Count(impl => impl.GetType() == typeof(CustomService2Implementation3)));

            Assert.AreEqual(1, impls3.Count(impl => impl.GetType() == typeof(CustomService3Implementation1)));
            Assert.AreEqual(1, impls3.Count(impl => impl.GetType() == typeof(CustomService3Implementation2)));
        }

        [Test]
        public void CustomObjectLifeTimeHandling()
        {
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

            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation>();
            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation2>();
            RedisServiceFactory.Register<ICustomService, CustomServiceImplementation3>();

            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation1>();
            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation2>();
            RedisServiceFactory.Register<ICustomService2, CustomService2Implementation3>();

            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation1>();
            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation2>();
            RedisServiceFactory.Register<ICustomService3, CustomService3Implementation3>();

            IEnumerable<ICustomService> impls1 = RedisServiceFactory.GetImplementations<ICustomService>();
            IEnumerable<ICustomService2> impls2 = RedisServiceFactory.GetImplementations<ICustomService2>();
            IEnumerable<ICustomService3> impls3 = RedisServiceFactory.GetImplementations<ICustomService3>();

            HashSet<object> allImpls = new HashSet<object>();

            foreach (ICustomService impl in impls1)
                allImpls.Add(impl);
            foreach (ICustomService2 impl in impls2)
                Assert.IsTrue(allImpls.Add(impl));
            foreach (ICustomService3 impl in impls3)
                Assert.IsTrue(allImpls.Add(impl));

            HashSet<object> impls1Hash = new HashSet<object>(RedisServiceFactory.GetImplementations<ICustomService>());
            HashSet<object> impls2Hash = new HashSet<object>(RedisServiceFactory.GetImplementations<ICustomService2>());
            HashSet<object> impls3Hash = new HashSet<object>(RedisServiceFactory.GetImplementations<ICustomService3>());

            impls1Hash.IntersectWith(allImpls);
            impls2Hash.IntersectWith(allImpls);
            impls3Hash.IntersectWith(allImpls);

            Assert.AreEqual(3, impls1Hash.Count);
            Assert.AreEqual(3, impls2Hash.Count);
            Assert.AreEqual(3, impls3Hash.Count);
        }

        [Test]
        public void NoServiceImplementationMustNotFail()
        {
            Assert.DoesNotThrow(() => RedisServiceFactory.GetImplementations<ICustomService>());
        }
    }
}