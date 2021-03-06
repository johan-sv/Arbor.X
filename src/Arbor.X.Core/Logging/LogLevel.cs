﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Arbor.X.Core.Logging
{
    public struct LogLevel : IEquatable<LogLevel>
    {
        public static readonly LogLevel Critical = new LogLevel("critical", "Critical", 1);
        public static readonly LogLevel Error = new LogLevel("error", "Error", 2);
        public static readonly LogLevel Warning = new LogLevel("warning", "Warning", 4);
        public static readonly LogLevel Information = new LogLevel("information", "Information", 8);
        public static readonly LogLevel Verbose = new LogLevel("verbose", "Verbose", 16);
        public static readonly LogLevel Debug = new LogLevel("debug", "Debug", 32);
        private readonly string _displayName;
        private readonly string _invariantName;
        private readonly int _level;

        private LogLevel(string invariantName, string displayName, int level)
        {
            _invariantName = invariantName;
            _displayName = displayName;
            _level = level;
        }

        public static LogLevel Default => Information;

        public static IEnumerable<LogLevel> AllValues
        {
            get
            {
                yield return Critical;
                yield return Error;
                yield return Warning;
                yield return Information;
                yield return Verbose;
                yield return Debug;
            }
        }

        public string DisplayName => _displayName ?? Default.DisplayName;

        public int Level => _level == 0 ? Default._level : _level;

        public string InvariantName => _invariantName ?? Default.InvariantName;

        public static implicit operator string(LogLevel logLevel)
        {
            return logLevel.DisplayName;
        }

        public static bool operator ==(LogLevel left, LogLevel right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(LogLevel left, LogLevel right)
        {
            return !left.Equals(right);
        }

        public static LogLevel TryParse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Default;
            }

            LogLevel found = AllValues.SingleOrDefault(
                level => level._invariantName.Equals(value, StringComparison.InvariantCultureIgnoreCase));

            return found;
        }

        public static LogLevel TryParse(int value)
        {
            LogLevel found = AllValues.SingleOrDefault(
                level => level._level == value);

            return found;
        }

        public bool Equals(LogLevel other)
        {
            return Level == other.Level;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is LogLevel && Equals((LogLevel)obj);
        }

        public override int GetHashCode()
        {
            return Level;
        }

        public override string ToString()
        {
            return DisplayName;
        }

        [Pure]
        public bool IsLogging(LogLevel logLevel)
        {
            return Level >= logLevel.Level;
        }
    }
}
