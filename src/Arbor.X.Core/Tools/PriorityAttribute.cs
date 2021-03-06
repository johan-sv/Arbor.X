﻿using System;

namespace Arbor.X.Core.Tools
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PriorityAttribute : Attribute
    {
        public PriorityAttribute(int priority, bool runAlways = false)
        {
            Priority = priority;
            RunAlways = runAlways;
        }

        public int Priority { get; }

        public bool RunAlways { get; }
    }
}
