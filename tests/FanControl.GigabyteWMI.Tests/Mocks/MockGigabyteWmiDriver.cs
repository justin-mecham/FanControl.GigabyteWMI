using System;
using System.Collections.Generic;
using FanControl.GigabyteWMI.Hardware;

namespace FanControl.GigabyteWMI.Tests.Mocks
{
    public class MockGigabyteWmiDriver : IGigabyteWmiDriver
    {
        public bool IsConnected { get; set; } = true;
        public Dictionary<string, byte> LastSetMethods { get; } = new Dictionary<string, byte>();
        public List<(string Method, byte Value)> SetMethodHistory { get; } = new List<(string Method, byte Value)>();
        public Dictionary<string, uint> GetMethodReturns { get; } = new Dictionary<string, uint>();
        public List<string> CallLog { get; } = new List<string>();

        public MockGigabyteWmiDriver()
        {
            GetMethodReturns["getRpm1"] = 2200;
            GetMethodReturns["getRpm2"] = 2400;
            GetMethodReturns["getCpuTemp"] = 48;
            GetMethodReturns["getGpuTemp1"] = 42;
        }

        public bool EnsureConnected()
        {
            CallLog.Add("EnsureConnected");
            return IsConnected;
        }

        public void SetMethod(string methodName, byte value)
        {
            LastSetMethods[methodName] = value;
            SetMethodHistory.Add((methodName, value));
            CallLog.Add($"SetMethod({methodName}, {value})");
        }

        public uint GetMethod(string methodName)
        {
            CallLog.Add($"GetMethod({methodName})");
            return GetMethodReturns.TryGetValue(methodName, out uint val) ? val : 0;
        }

        public void Log(string message)
        {
            CallLog.Add($"Log: {message}");
        }
    }
}
