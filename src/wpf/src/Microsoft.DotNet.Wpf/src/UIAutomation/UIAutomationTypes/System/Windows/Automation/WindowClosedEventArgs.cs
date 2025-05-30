// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Description: WindowClosedEventArgs event args class

namespace System.Windows.Automation
{
    /// <summary>
    /// WindowClosedEventArgs event args class
    /// </summary>
#if (INTERNAL_COMPILE)
    internal sealed class WindowClosedEventArgs  : AutomationEventArgs
#else
    public sealed class WindowClosedEventArgs  : AutomationEventArgs
#endif
    {
        //------------------------------------------------------
        //
        //  Constructors
        //
        //------------------------------------------------------
 
        #region Constructors

        /// <summary>
        /// Constructor for top-level window event args.
        /// </summary>
        public WindowClosedEventArgs (int [] runtimeId) 
            : base(WindowPatternIdentifiers.WindowClosedEvent)
        {
            ArgumentNullException.ThrowIfNull(runtimeId);
            _runtimeId = (int[])runtimeId.Clone();
        }

        #endregion Constructors


        //------------------------------------------------------
        //
        //  Public Properties
        //
        //------------------------------------------------------
 
        #region Public Properties

        /// <summary>
        /// Returns the Windows UI Automation runtime identifier
        /// </summary>
        public int [] GetRuntimeId()
        { 
            return (int [])_runtimeId.Clone();
        }

        #endregion Public Properties


        //------------------------------------------------------
        //
        //  Private Fields
        //
        //------------------------------------------------------
 
        #region Private Fields

        private int [] _runtimeId;

        #endregion Private Fields
    }
}
