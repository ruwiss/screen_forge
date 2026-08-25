using NAudio.CoreAudioApi;

namespace ScreenForge.Record;

public readonly record struct AudioDeviceInfo(string Id, string Name);

public static class AudioDevices
{
    public static IReadOnlyList<AudioDeviceInfo> ListMicrophones()
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                    list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            }
        }
        catch
        {
            // Ses aygıtı yok / erişim yok.
        }
        return list;
    }

    public static string? DefaultMicrophoneId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return device?.ID;
        }
        catch
        {
            return null;
        }
    }
}
