// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//

namespace System.Windows.Input
{
    /// <summary>
    ///     Provides information about the end of a manipulation.
    /// </summary>
    public sealed class ManipulationCompletedEventArgs : InputEventArgs
    {
        /// <summary>
        ///     Instantiates a new instance of this class.
        /// </summary>
        internal ManipulationCompletedEventArgs(
            ManipulationDevice manipulationDevice,
            int timestamp, 
            IInputElement manipulationContainer,
            Point origin, 
            ManipulationDelta total,
            ManipulationVelocities velocities,
            bool isInertial)
            : base(manipulationDevice, timestamp)
        {
            ArgumentNullException.ThrowIfNull(total);

            ArgumentNullException.ThrowIfNull(velocities);

            RoutedEvent = Manipulation.ManipulationCompletedEvent;

            ManipulationContainer = manipulationContainer;
            ManipulationOrigin = origin;
            TotalManipulation = total;
            FinalVelocities = velocities;
            IsInertial = isInertial;
        }

        /// <summary>
        ///     Invokes a handler of this event.
        /// </summary>
        protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
        {
            ArgumentNullException.ThrowIfNull(genericHandler);

            ArgumentNullException.ThrowIfNull(genericTarget);

            if (RoutedEvent == Manipulation.ManipulationCompletedEvent)
            {
                ((EventHandler<ManipulationCompletedEventArgs>)genericHandler)(genericTarget, this);
            }
            else
            {
                base.InvokeEventHandler(genericHandler, genericTarget);
            }
        }

        /// <summary>
        ///     Whether the event was generated due to inertia.
        /// </summary>
        public bool IsInertial
        {
            get;
            private set;
        }

        /// <summary>
        ///     Defines the coordinate space of the other properties.
        /// </summary>
        public IInputElement ManipulationContainer
        {
            get;
            private set;
        }

        /// <summary>
        ///     Returns the value of the origin.
        /// </summary>
        public Point ManipulationOrigin
        {
            get;
            private set;
        }

        /// <summary>
        ///     Returns the cumulative transformation associated with the manipulation.
        /// </summary>
        public ManipulationDelta TotalManipulation
        {
            get;
            private set;
        }

        /// <summary>
        ///     Returns the current velocities associated with a manipulation.
        /// </summary>
        public ManipulationVelocities FinalVelocities
        {
            get;
            private set;
        }

        /// <summary>
        ///     Method to cancel the Manipulation
        /// </summary>
        /// <returns>A bool indicating the success of Cancel</returns>
        public bool Cancel()
        {
            if (!IsInertial)
            {
                RequestedCancel = true;
                return true;
            }
            return false;
        }

        /// <summary>
        ///     A handler Requested to cancel the Manipulation
        /// </summary>
        internal bool RequestedCancel
        {
            get;
            private set;
        }

        /// <summary>
        ///     The Manipulators for this manipulation.
        /// </summary>
        public IEnumerable<IManipulator> Manipulators
        {
            get
            {
                if (_manipulators == null)
                {
                    _manipulators = ((ManipulationDevice)Device).GetManipulatorsReadOnly();
                }
                return _manipulators;
            }
        }

        private IEnumerable<IManipulator> _manipulators;
    }
}
