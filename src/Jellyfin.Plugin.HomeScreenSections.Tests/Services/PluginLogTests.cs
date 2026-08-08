using System.Net;
using System.Reflection;
using Jellyfin.Plugin.HomeScreenSections.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.HomeScreenSections.Tests.Services;

/// <summary>
/// Exercises every source-generated LoggerMessage method in PluginLog. The generated
/// bodies (level checks + formatted log calls) count toward coverage only when invoked
/// with an enabled logger, so a capturing logger runs each one once.
/// </summary>
public class PluginLogTests
{
    [Fact]
    public void All_generated_log_methods_execute_with_an_enabled_logger()
    {
        CapturingLogger logger = new CapturingLogger();

        List<MethodInfo> logMethods = EnumerateLogMethods();
        Assert.True(logMethods.Count >= 60, $"Expected a large generated log surface, found {logMethods.Count} methods.");

        foreach (MethodInfo method in logMethods)
        {
            object?[] arguments = method.GetParameters()
                .Select(parameter => CreateArgument(parameter.ParameterType, logger))
                .ToArray();

            // Must not throw for any generated signature.
            method.Invoke(null, arguments);
        }

        // Every method produced exactly one formatted message.
        Assert.Equal(logMethods.Count, logger.Messages.Count);
        Assert.All(logger.Messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
    }

    [Fact]
    public void Generated_log_methods_skip_formatting_when_level_disabled()
    {
        // NullLogger reports every level as disabled, exercising the early-exit branch.
        MethodInfo method = EnumerateLogMethods().First();
        method.Invoke(null, CreateArgumentsFor(method, NullLogger.Instance));
    }

    private static List<MethodInfo> EnumerateLogMethods()
    {
        return typeof(PluginLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(PluginLog))
            .ToList();
    }

    private static object?[] CreateArgumentsFor(MethodInfo method, ILogger logger)
    {
        return method.GetParameters()
            .Select(parameter => CreateArgument(parameter.ParameterType, logger))
            .ToArray();
    }

    private static object? CreateArgument(Type type, ILogger logger)
    {
        if (type == typeof(ILogger))
        {
            return logger;
        }

        if (type == typeof(string))
        {
            return "value";
        }

        if (type == typeof(Exception))
        {
            return new InvalidOperationException("logged failure");
        }

        object? primitive = CreatePrimitiveArgument(type);
        if (primitive != null)
        {
            return primitive;
        }

        Type? underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            return CreateArgument(underlying, logger);
        }

        if (type.IsEnum)
        {
            Array values = Enum.GetValues(type);
            return values.GetValue(0);
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, 0);
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        return null;
    }

    private static object? CreatePrimitiveArgument(Type type)
    {
        if (type == typeof(int))
        {
            return 1;
        }

        if (type == typeof(long))
        {
            return 1L;
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(double))
        {
            return 1.5d;
        }

        if (type == typeof(float))
        {
            return 1.5f;
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (type == typeof(DateTime))
        {
            return DateTime.UtcNow;
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.FromSeconds(1);
        }

        if (type == typeof(HttpStatusCode))
        {
            return HttpStatusCode.OK;
        }

        return null;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
