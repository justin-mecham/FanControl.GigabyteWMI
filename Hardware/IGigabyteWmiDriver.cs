namespace FanControl.GigabyteWMI.Hardware
{
    /// <summary>
    /// Low-level interface for WMI ACPI communication with GB_WMIACPI classes.
    /// </summary>
    public interface IGigabyteWmiDriver
    {
        bool EnsureConnected();
        void SetMethod(string methodName, byte value);
        uint GetMethod(string methodName);
        void Log(string message);
    }
}
