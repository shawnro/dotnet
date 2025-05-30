// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#define TRACE

//
// Description: Defines TraceShell class, for providing debugging information
//              for Shell integration
//

namespace MS.Internal
{
    /// <summary>
    /// Provides a central mechanism for providing debugging information
    /// to aid programmers in using Shell integration features.
    /// Helpers are defined here.
    /// The rest of the class is generated; see also: AvTraceMessage.txt and genTraceStrings.pl
    /// </summary>
    internal static partial class TraceShell
    {
        static TraceShell()
        {
            // This tells tracing that IsEnabled should be true if we're in the debugger,
            // even if the registry flag isn't turned on.  By default, IsEnabled is only
            // true if the registry is set.
            _avTrace.EnabledByDebugger = true;
        }
    }
}


