using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Recognition; // Windows Speech API
using System.Threading;
using System.Threading.Tasks;

namespace Redball.UI.Services;

/// <summary>
/// Voice command recognition service for hands-free Redball control.
/// Integrates with Windows Speech API for voice-activated commands.
/// </summary>
public class VoiceCommandService : IDisposable
{
    private static readonly Lazy<VoiceCommandService> _instance = new(() => new VoiceCommandService());
    public static VoiceCommandService Instance => _instance.Value;

    private SpeechRecognitionEngine? _recognizer;
    private bool _isInitialized;
    private bool _isListening;
    private readonly Dictionary<string, VoiceCommand> _commands;

    public event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;
    public event EventHandler<VoiceRecognitionStateEventArgs>? StateChanged;

    public bool IsEnabled { get; set; }
    public bool IsAvailable => _isInitialized;
    public bool IsListening => _isListening;
    public IReadOnlyDictionary<string, VoiceCommand> RegisteredCommands => _commands;

    private VoiceCommandService()
    {
        _commands = new Dictionary<string, VoiceCommand>(StringComparer.OrdinalIgnoreCase);
        
        try
        {
            InitializeRecognizer();
        }
        catch (Exception ex)
        {
            Logger.Warning("VoiceCommandService", $"Speech recognition not available: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes the speech recognition engine.
    /// </summary>
    private void InitializeRecognizer()
    {
        try
        {
            // Check if speech recognition is available
            if (!IsSpeechRecognitionAvailable())
            {
                Logger.Info("VoiceCommandService", "Speech recognition engine not available on this system");
                return;
            }

            _recognizer = new SpeechRecognitionEngine(new System.Globalization.CultureInfo("en-US"));
            _recognizer.SpeechRecognized += OnSpeechRecognized;
            _recognizer.SpeechRecognitionRejected += OnSpeechRejected;
            _recognizer.RecognizeCompleted += OnRecognizeCompleted;

            // Load default grammar
            LoadDefaultGrammar();

            _isInitialized = true;
            Logger.Info("VoiceCommandService", "Speech recognition initialized successfully");
        }
        catch (PlatformNotSupportedException)
        {
            Logger.Warning("VoiceCommandService", "Speech recognition not supported on this platform");
        }
        catch (Exception ex)
        {
            Logger.Error("VoiceCommandService", "Failed to initialize speech recognition", ex);
        }
    }

    /// <summary>
    /// Starts listening for voice commands.
    /// </summary>
    public bool StartListening()
    {
        if (!_isInitialized || _recognizer == null)
        {
            Logger.Warning("VoiceCommandService", "Cannot start listening - speech recognition not available");
            return false;
        }

        if (_isListening)
            return true;

        try
        {
            _recognizer.SetInputToDefaultAudioDevice();
            _recognizer.RecognizeAsync(RecognizeMode.Multiple);
            _isListening = true;

            StateChanged?.Invoke(this, new VoiceRecognitionStateEventArgs
            {
                IsListening = true,
                Timestamp = DateTime.UtcNow
            });

            Logger.Info("VoiceCommandService", "Started listening for voice commands");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("VoiceCommandService", "Failed to start listening", ex);
            return false;
        }
    }

    /// <summary>
    /// Stops listening for voice commands.
    /// </summary>
    public void StopListening()
    {
        if (!_isListening || _recognizer == null)
            return;

        try
        {
            _recognizer.RecognizeAsyncStop();
            _isListening = false;

            StateChanged?.Invoke(this, new VoiceRecognitionStateEventArgs
            {
                IsListening = false,
                Timestamp = DateTime.UtcNow
            });

            Logger.Info("VoiceCommandService", "Stopped listening for voice commands");
        }
        catch (Exception ex)
        {
            Logger.Error("VoiceCommandService", "Error stopping listening", ex);
        }
    }

    /// <summary>
    /// Registers a voice command with associated action.
    /// </summary>
    public void RegisterCommand(string phrase, string description, Action action, bool requiresConfirmation = false)
    {
        if (string.IsNullOrWhiteSpace(phrase))
            throw new ArgumentException("Command phrase cannot be empty", nameof(phrase));

        var command = new VoiceCommand
        {
            Phrase = phrase,
            Description = description,
            Action = action,
            RequiresConfirmation = requiresConfirmation
        };

        _commands[phrase] = command;

        // Update grammar if already initialized
        if (_isInitialized && _recognizer != null)
        {
            LoadDefaultGrammar();
        }

        Logger.Debug("VoiceCommandService", $"Registered voice command: '{phrase}'");
    }

    /// <summary>
    /// Unregisters a voice command.
    /// </summary>
    public void UnregisterCommand(string phrase)
    {
        _commands.Remove(phrase);
        
        if (_isInitialized && _recognizer != null)
        {
            LoadDefaultGrammar();
        }

        Logger.Debug("VoiceCommandService", $"Unregistered voice command: '{phrase}'");
    }

    /// <summary>
    /// Registers default Redball voice commands.
    /// </summary>
    public void RegisterDefaultCommands()
    {
        RegisterCommand(
            "start keep awake",
            "Start keep-awake mode",
            () => KeepAwakeService.Instance.SetActive(true));

        RegisterCommand(
            "stop keep awake",
            "Stop keep-awake mode",
            () => KeepAwakeService.Instance.SetActive(false));

        RegisterCommand(
            "toggle keep awake",
            "Toggle keep-awake mode",
            () => KeepAwakeService.Instance.Toggle());

        RegisterCommand(
            "start timer",
            "Start timed keep-awake session",
            () => KeepAwakeService.Instance.StartTimed(60));

        RegisterCommand(
            "show status",
            "Show current keep-awake status",
            () => Logger.Info("VoiceCommand", $"Status: {KeepAwakeService.Instance.IsActive}"));

        RegisterCommand(
            "enable battery mode",
            "Enable battery-aware mode",
            () => { ConfigService.Instance.Config.BatteryAware = true; });

        RegisterCommand(
            "disable battery mode",
            "Disable battery-aware mode",
            () => { ConfigService.Instance.Config.BatteryAware = false; });

        Logger.Info("VoiceCommandService", "Default commands registered");
    }

    /// <summary>
    /// Loads grammar for speech recognition based on registered commands.
    /// </summary>
    private void LoadDefaultGrammar()
    {
        if (_recognizer == null) return;

        try
        {
            _recognizer.UnloadAllGrammars();

            // Create grammar builder
            var grammarBuilder = new GrammarBuilder();
            grammarBuilder.AppendWildcard();
            
            // Add choices for registered commands
            if (_commands.Any())
            {
                var choices = new Choices(_commands.Keys.ToArray());
                grammarBuilder = new GrammarBuilder(choices);
            }
            else
            {
                // Default commands if none registered
                var defaultChoices = new Choices(new[] { 
                    "start keep awake", 
                    "stop keep awake", 
                    "toggle keep awake",
                    "show status" 
                });
                grammarBuilder = new GrammarBuilder(defaultChoices);
            }

            var grammar = new Grammar(grammarBuilder);
            _recognizer.LoadGrammar(grammar);

            // Add confirmation grammar
            var confirmationChoices = new Choices(new[] { "yes", "no", "confirm", "cancel" });
            var confirmationGrammar = new Grammar(new GrammarBuilder(confirmationChoices));
            confirmationGrammar.Name = "Confirmation";
            _recognizer.LoadGrammar(confirmationGrammar);

            Logger.Debug("VoiceCommandService", $"Loaded grammar with {_commands.Count} commands");
        }
        catch (Exception ex)
        {
            Logger.Error("VoiceCommandService", "Failed to load grammar", ex);
        }
    }

    /// <summary>
    /// Handles recognized speech events.
    /// </summary>
    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var phrase = e.Result.Text;
        var confidence = e.Result.Confidence;

        Logger.Debug("VoiceCommandService", $"Recognized: '{phrase}' (confidence: {confidence:P})");

        // Check confidence threshold
        if (confidence < 0.7)
        {
            Logger.Debug("VoiceCommandService", "Recognition confidence too low, ignoring");
            return;
        }

        // Handle confirmation responses
        if (e.Result.Grammar.Name == "Confirmation")
        {
            // Handle yes/no confirmation
            return;
        }

        // Find and execute command
        var command = _commands.Values.FirstOrDefault(c => 
            phrase.Contains(c.Phrase, StringComparison.OrdinalIgnoreCase));

        if (command != null)
        {
            CommandRecognized?.Invoke(this, new VoiceCommandRecognizedEventArgs
            {
                Command = command,
                Confidence = confidence,
                RecognizedAt = DateTime.UtcNow
            });

            try
            {
                if (command.RequiresConfirmation)
                {
                    // Show confirmation UI or request verbal confirmation
                    Logger.Info("VoiceCommandService", $"Command '{command.Phrase}' requires confirmation");
                }
                else
                {
                    command.Action?.Invoke();
                    Logger.Info("VoiceCommandService", $"Executed command: '{command.Phrase}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("VoiceCommandService", $"Error executing command '{command.Phrase}'", ex);
            }
        }
    }

    private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        Logger.Debug("VoiceCommandService", $"Speech rejected: '{e.Result.Text}'");
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (e.Error != null)
        {
            Logger.Error("VoiceCommandService", "Recognition error", e.Error);
        }

        // Restart listening if it was stopped due to error
        if (_isListening && e.Error != null)
        {
            Thread.Sleep(1000);
            StartListening();
        }
    }

    private bool IsSpeechRecognitionAvailable()
    {
        try
        {
            // Check if speech recognition engine is available
            var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
            return recognizers.Any();
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        StopListening();
        _recognizer?.Dispose();
        _recognizer = null;
        _isInitialized = false;
    }
}

/// <summary>
/// Represents a voice command with associated action.
/// </summary>
public class VoiceCommand
{
    public string Phrase { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Action? Action { get; set; }
    public bool RequiresConfirmation { get; set; }
}

/// <summary>
/// Event args for recognized voice commands.
/// </summary>
public class VoiceCommandRecognizedEventArgs : EventArgs
{
    public VoiceCommand Command { get; set; } = new();
    public float Confidence { get; set; }
    public DateTime RecognizedAt { get; set; }
}

/// <summary>
/// Event args for voice recognition state changes.
/// </summary>
public class VoiceRecognitionStateEventArgs : EventArgs
{
    public bool IsListening { get; set; }
    public DateTime Timestamp { get; set; }
}
