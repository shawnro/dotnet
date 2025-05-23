using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit.Abstractions;

#if XUNIT_FRAMEWORK
namespace Xunit.Sdk
#else
namespace Xunit
#endif
{
    /// <summary>
    /// Utility classes for dealing with Exception objects.
    /// </summary>
    public static class ExceptionUtility
    {
        /// <summary>
        /// Combines multiple levels of messages into a single message.
        /// </summary>
        /// <param name="failureInfo">The failure information from which to get the messages.</param>
        /// <returns>The combined string.</returns>
        public static string CombineMessages(IFailureInformation failureInfo)
        {
            return GetMessage(failureInfo, 0, 0);
        }

        /// <summary>
        /// Combines multiple levels of stack traces into a single stack trace.
        /// </summary>
        /// <param name="failureInfo">The failure information from which to get the stack traces.</param>
        /// <returns>The combined string.</returns>
        public static string CombineStackTraces(IFailureInformation failureInfo)
        {
            return GetStackTrace(failureInfo, 0);
        }

#if XUNIT_FRAMEWORK
        static readonly ConcurrentDictionary<Type, PropertyInfo> innerExceptionsPropertyByType = new();

        static IEnumerable<Exception> GetInnerExceptions(Exception ex)
        {
            if (ex is AggregateException aggEx)
                return aggEx.InnerExceptions;

            var prop = innerExceptionsPropertyByType.GetOrAdd(
                ex.GetType(),
                t => t.GetRuntimeProperties().FirstOrDefault(p => p.Name == "InnerExceptions" && p.CanRead)
            );

            return prop?.GetValue(ex) as IEnumerable<Exception>;
        }
#endif

        static bool ExcludeStackFrame(string stackFrame)
        {
            Guard.ArgumentNotNull("stackFrame", stackFrame);

            return stackFrame.StartsWith("at Xunit.", StringComparison.Ordinal);
        }

        static string FilterStackTrace(string stack)
        {
            if (stack == null)
                return null;

            var results = new List<string>();

            foreach (var line in SplitLines(stack))
            {
                var trimmedLine = line.TrimStart();
                if (!ExcludeStackFrame(trimmedLine))
                    results.Add(line);
            }

            return string.Join(Environment.NewLine, results.ToArray());
        }

        static string GetAt(string[] values, int index)
        {
            if (values == null || index < 0 || values.Length <= index)
                return string.Empty;

            return values[index] ?? string.Empty;
        }

        static int GetAt(int[] values, int index)
        {
            if (values == null || values.Length <= index)
                return -1;

            return values[index];
        }

        static string GetMessage(IFailureInformation failureInfo, int index, int level)
        {
            var result = "";

            if (level > 0)
            {
                for (var idx = 0; idx < level; idx++)
                    result += "----";

                result += " ";
            }

            var exceptionType = GetAt(failureInfo.ExceptionTypes, index);
            if (GetNamespace(exceptionType) != "Xunit.Sdk")
                result += exceptionType + " : ";

            result += GetAt(failureInfo.Messages, index);

            for (var subIndex = index + 1; subIndex < failureInfo.ExceptionParentIndices.Length; ++subIndex)
                if (GetAt(failureInfo.ExceptionParentIndices, subIndex) == index)
                    result += Environment.NewLine + GetMessage(failureInfo, subIndex, level + 1);

            return result;
        }

        static string GetNamespace(string exceptionType)
        {
            var nsIndex = exceptionType.LastIndexOf('.');
            if (nsIndex > 0)
                return exceptionType.Substring(0, nsIndex);

            return "";
        }

        static string GetStackTrace(IFailureInformation failureInfo, int index)
        {
            var result = FilterStackTrace(GetAt(failureInfo.StackTraces, index));

            var children = new List<int>();
            for (var subIndex = index + 1; subIndex < failureInfo.ExceptionParentIndices.Length; ++subIndex)
                if (GetAt(failureInfo.ExceptionParentIndices, subIndex) == index)
                    children.Add(subIndex);

            if (children.Count > 1)
                for (var idx = 0; idx < children.Count; ++idx)
                    result += string.Format(
                        CultureInfo.CurrentCulture,
                        "{0}----- Inner Stack Trace #{1} ({2}) -----{0}{3}",
                        Environment.NewLine,
                        idx + 1,
                        GetAt(failureInfo.ExceptionTypes, children[idx]),
                        GetStackTrace(failureInfo, children[idx])
                    );
            else if (children.Count == 1)
                result += string.Format(
                    CultureInfo.CurrentCulture,
                    "{0}----- Inner Stack Trace -----{0}{1}",
                    Environment.NewLine,
                    GetStackTrace(failureInfo, children[0])
                );

            return result;
        }

        // Our own custom string.Split because Silverlight/CoreCLR doesn't support the version we were using
        static IEnumerable<string> SplitLines(string input)
        {
            while (true)
            {
                var idx = input.IndexOf(Environment.NewLine, StringComparison.Ordinal);

                if (idx < 0)
                {
                    yield return input;
                    break;
                }

                yield return input.Substring(0, idx);
                input = input.Substring(idx + Environment.NewLine.Length);
            }
        }

        /// <summary>
        /// Unwraps exceptions and their inner exceptions.
        /// </summary>
        /// <param name="ex">The exception to be converted.</param>
        /// <returns>The failure information.</returns>
        public static IFailureInformation ConvertExceptionToFailureInformation(Exception ex)
        {
            var exceptionTypes = new List<string>();
            var messages = new List<string>();
            var stackTraces = new List<string>();
            var indices = new List<int>();

            ConvertExceptionToFailureInformation(ex, -1, exceptionTypes, messages, stackTraces, indices);

            return new FailureInformation
            {
                ExceptionParentIndices = indices.ToArray(),
                ExceptionTypes = exceptionTypes.ToArray(),
                Messages = messages.ToArray(),
                StackTraces = stackTraces.ToArray(),
            };
        }

        static void ConvertExceptionToFailureInformation(Exception ex, int parentIndex, List<string> exceptionTypes, List<string> messages, List<string> stackTraces, List<int> indices)
        {
            var myIndex = exceptionTypes.Count;

            try
            {
                exceptionTypes.Add(ex.GetType().FullName);
            }
            catch (Exception thrown)
            {
                exceptionTypes.Add(string.Format(CultureInfo.CurrentCulture, "<exception thrown while retrieving exception type: {0}>", thrown.Message));
            }

            try
            {
                messages.Add(ex.Message);
            }
            catch (Exception thrown)
            {
                messages.Add(string.Format(CultureInfo.CurrentCulture, "<exception thrown while retrieving exception message: {0}>", thrown.Message));
            }

            try
            {
                stackTraces.Add(ex.StackTrace);
            }
            catch (Exception thrown)
            {
                stackTraces.Add(string.Format(CultureInfo.CurrentCulture, "<exception thrown while retrieving exception stack trace: {0}>", thrown.Message));
            }

            indices.Add(parentIndex);

            try
            {
#if XUNIT_FRAMEWORK
                var innerExceptions = GetInnerExceptions(ex);
                if (innerExceptions != null)
                    foreach (var innerException in innerExceptions)
                        ConvertExceptionToFailureInformation(innerException, myIndex, exceptionTypes, messages, stackTraces, indices);
                else
#endif
                if (ex.InnerException != null)
                    ConvertExceptionToFailureInformation(ex.InnerException, myIndex, exceptionTypes, messages, stackTraces, indices);
            }
            catch { }
        }

        class FailureInformation : IFailureInformation
        {
            public string[] ExceptionTypes { get; set; }
            public string[] Messages { get; set; }
            public string[] StackTraces { get; set; }
            public int[] ExceptionParentIndices { get; set; }
        }
    }
}
