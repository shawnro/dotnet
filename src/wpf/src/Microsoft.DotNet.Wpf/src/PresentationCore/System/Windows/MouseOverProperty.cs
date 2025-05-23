﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Input;

namespace System.Windows
{
    /////////////////////////////////////////////////////////////////////////

    internal class MouseOverProperty : ReverseInheritProperty
    {
        /////////////////////////////////////////////////////////////////////

        internal MouseOverProperty() : base(
            UIElement.IsMouseOverPropertyKey,
            CoreFlags.IsMouseOverCache,
            CoreFlags.IsMouseOverChanged)
        {
        }

        /////////////////////////////////////////////////////////////////////

        internal override void FireNotifications(UIElement uie, ContentElement ce, UIElement3D uie3D, bool oldValue)
        {
            // Before we fire the mouse event we need to figure if the notification is still relevant. 
            // This is because it is possible that the mouse state has changed during the previous 
            // property engine callout. Example: Consider a MessageBox being displayed during the 
            // IsMouseOver OnPropertyChanged override.
            
            bool shouldFireNotification = false;
            if (uie != null)
            {
                shouldFireNotification = (!oldValue && uie.IsMouseOver) || (oldValue && !uie.IsMouseOver);
            }
            else if (ce != null)
            {
                shouldFireNotification = (!oldValue && ce.IsMouseOver) || (oldValue && !ce.IsMouseOver);
            }
            else if (uie3D != null)
            {
                shouldFireNotification = (!oldValue && uie3D.IsMouseOver) || (oldValue && !uie3D.IsMouseOver);
            }

            if (shouldFireNotification)
            {
                MouseEventArgs mouseEventArgs = new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount, Mouse.PrimaryDevice.StylusDevice)
                {
                    RoutedEvent = oldValue ? Mouse.MouseLeaveEvent : Mouse.MouseEnterEvent
                };

                if (uie != null)
                {
                    uie.RaiseEvent(mouseEventArgs);
                }
                else if (ce != null)
                {
                    ce.RaiseEvent(mouseEventArgs);
                }
                else
                {
                    uie3D?.RaiseEvent(mouseEventArgs);
                }
            }
        }
    }
}

