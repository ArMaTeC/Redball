using System;
using System.Speech.Synthesis;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Provides text-to-speech functionality for TypeThing using Windows SAPI.
/// Optionally reads aloud what is being typed for presentations or accessibility.
/// </summary>
public class TextToSpeechService
{
    private static readonly Lazy<TextToSpeechService> _instance = new(() => new TextToSpeechService());
    public static TextToSpeechService Instance => _instance.Value;

    private SpeechSynthesizer? _synth;
    private bool _enabled;

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (value)
                EnsureSynth();
        }
    }

    public int Rate { get; set; } = 0; // -10 to 10
    public int Volume { get; set; } = 100; // 0 to 100
    public bool IsSpeaking => _synth?.State == SynthesizerState.Speaking;

    private TextToSpeechService()
    {
        Logger.Verbose("TextToSpeechService", "Instance created");
    }

    private void EnsureSynth()
    {
        if (_synth != null) return;
        try
        {
            _synth = new SpeechSynthesizer();
            _synth.SetOutputToDefaultAudioDevice();
            Logger.Info("TextToSpeechService", "SpeechSynthesizer initialized");
        }
        catch (Exception ex)
        {
            Logger.Error("TextToSpeechService", "Failed to initialize SpeechSynthesizer", ex);
            _synth = null;
        }
    }

    /// <summary>
    /// Speaks the given text asynchronously.
    /// </summary>
    public void SpeakAsync(string text)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(text)) return;
        EnsureSynth();
        if (_synth == null) return;

        try
        {
            _synth.Rate = Rate;
            _synth.Volume = Volume;
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync(text);
            Logger.Debug("TextToSpeechService", $"Speaking: {text.Substring(0, Math.Min(50, text.Length))}...");
        }
        catch (Exception ex)
        {
            Logger.Error("TextToSpeechService", "SpeakAsync failed", ex);
        }
    }

    /// <summary>
    /// Stops any current speech.
    /// </summary>
    public void Stop()
    {
        try
        {
            _synth?.SpeakAsyncCancelAll();
        }
        catch (Exception ex)
        {
            Logger.Debug("TextToSpeechService", $"Failed to cancel speech: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets a list of available voice names.
    /// </summary>
    public string[] GetAvailableVoices()
    {
        EnsureSynth();
        if (_synth == null) return Array.Empty<string>();

        try
        {
            var voices = _synth.GetInstalledVoices();
            var names = new string[voices.Count];
            for (int i = 0; i < voices.Count; i++)
                names[i] = voices[i].VoiceInfo.Name;
            return names;
        }
        catch (Exception ex)
        {
            Logger.Debug("TextToSpeechService", $"Failed to get available voices: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        _synth?.Dispose();
        _synth = null;
    }
}
