// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Description: Class to create a queue on its own thread.

namespace MS.Internal.Automation
{
    // Worker class used to handle WinEvents
    internal class WinEventQueueItem : QueueItem
    {
        //------------------------------------------------------
        //
        //  Constructors
        //
        //------------------------------------------------------
 
        #region Constructors

        internal WinEventQueueItem(WinEventWrap winEventWrap, int state)
        {
            _winEventWrap = winEventWrap;
            _state = state;
        }

        #endregion Constructors


        //------------------------------------------------------
        //
        //  Public Constants / Readonly Fields
        //
        //------------------------------------------------------
 
        #region Public Constants and Readonly Fields

        internal const int StartListening = 1;
        internal const int StopListening = 2;

        #endregion Public Constants and Readonly Fields


        //------------------------------------------------------
        //
        //  Internal Methods
        //
        //------------------------------------------------------
 
        #region Internal Methods

        internal override void Process()
        {
            if (_state == StartListening)
            {
                _winEventWrap.StartListening();
            }
            else
            {
                _winEventWrap.StopListening();
            }
        }

        #endregion Internal Methods


        //------------------------------------------------------
        //
        //  Private Fields
        //
        //------------------------------------------------------
 
        #region Private Fields

        private WinEventWrap _winEventWrap;
        private int _state;

        #endregion Private Fields
    }
}
