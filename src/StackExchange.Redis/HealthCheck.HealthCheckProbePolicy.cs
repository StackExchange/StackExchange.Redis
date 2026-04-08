namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    /// <summary>
    /// Attempt to evaluate the outcome of a series of health check operations.
    /// </summary>
    public abstract class HealthCheckProbePolicy
    {
        /// <summary>
        /// Attempt to evaluate the policy given the current context.
        /// </summary>
        /// <param name="context">The state of the probes so far.</param>
        /// <returns>The result of the policy evaluation.</returns>
        public abstract HealthCheckResult Evaluate(in HealthCheckProbeContext context);

        /// <summary>
        /// Require all probes to succeed.
        /// </summary>
        public static HealthCheckProbePolicy AllSuccess => AllSuccessHealthCheckProbePolicy.Instance;

        /// <summary>
        /// Require at least one probe to succeed.
        /// </summary>
        public static HealthCheckProbePolicy AnySuccess => AnySuccessHealthCheckProbePolicy.Instance;

        /// <summary>
        /// Require a majority of probes to succeed.
        /// </summary>
        public static HealthCheckProbePolicy MajoritySuccess => MajoritySuccessHealthCheckProbePolicy.Instance;

        private sealed class AllSuccessHealthCheckProbePolicy : HealthCheckProbePolicy
        {
            public static readonly AllSuccessHealthCheckProbePolicy Instance = new();
            private AllSuccessHealthCheckProbePolicy() { }

            public override HealthCheckResult Evaluate(in HealthCheckProbeContext context)
            {
                // Fail as soon as we have any failure
                if (context.Failure > 0)
                {
                    return HealthCheckResult.Unhealthy;
                }

                // Succeed only when all probes have succeeded (no remaining)
                if (context.Remaining == 0)
                {
                    return HealthCheckResult.Healthy;
                }

                // Can't determine yet
                return HealthCheckResult.Inconclusive;
            }
        }

        private sealed class AnySuccessHealthCheckProbePolicy : HealthCheckProbePolicy
        {
            public static readonly AnySuccessHealthCheckProbePolicy Instance = new();
            private AnySuccessHealthCheckProbePolicy() { }

            public override HealthCheckResult Evaluate(in HealthCheckProbeContext context)
            {
                // Succeed as soon as we have any success
                if (context.Success > 0)
                {
                    return HealthCheckResult.Healthy;
                }

                // Fail only when all probes have failed (no remaining)
                if (context.Remaining == 0)
                {
                    return HealthCheckResult.Unhealthy;
                }

                // Can't determine yet
                return HealthCheckResult.Inconclusive;
            }
        }

        private sealed class MajoritySuccessHealthCheckProbePolicy : HealthCheckProbePolicy
        {
            public static readonly MajoritySuccessHealthCheckProbePolicy Instance = new();
            private MajoritySuccessHealthCheckProbePolicy() { }

            public override HealthCheckResult Evaluate(in HealthCheckProbeContext context)
            {
                int total = context.Success + context.Failure + context.Remaining;
                int majority = (total / 2) + 1;

                // Succeed as soon as we have enough successes for a majority
                if (context.Success >= majority)
                {
                    return HealthCheckResult.Healthy;
                }

                // Fail as soon as we have enough failures to make a majority impossible
                if (context.Failure >= majority)
                {
                    return HealthCheckResult.Unhealthy;
                }

                // Can't determine yet
                return HealthCheckResult.Inconclusive;
            }
        }
    }
}
