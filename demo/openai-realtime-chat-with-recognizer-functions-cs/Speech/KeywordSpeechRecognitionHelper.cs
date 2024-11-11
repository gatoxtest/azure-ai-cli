using System;
using System.Runtime.InteropServices;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

public class KeywordSpeechRecognitionHelper
{
    public KeywordSpeechRecognitionHelper(string speechKey, string speechRegion, string speechLanguage, string keywordFileName, string? inputFileName)
    {
        // Check to see if the input file exists
        if (inputFileName != null && !File.Exists(inputFileName))
        {
            Console.WriteLine($"ERROR: Cannot find audio input file: {inputFileName}");
            throw new ApplicationException($"Cannot find audio input file: {inputFileName}");
        }

        // Check to see if the keyword file exists
        if (!File.Exists(keywordFileName))
        {
            Console.WriteLine($"ERROR: Cannot find keyword file: {keywordFileName}");
            throw new ApplicationException($"Cannot find keyword file: {keywordFileName}");
        }

        // Create instances of a speech config, source language config, and audio config
        _config = SpeechConfig.FromSubscription(speechKey, speechRegion);
        _sourceLanguageConfig = SourceLanguageConfig.FromLanguage(speechLanguage);
        _audioConfig = inputFileName != null
            ? AudioConfig.FromWavFileInput(inputFileName)
            : AudioConfig.FromDefaultMicrophoneInput();

        // Create the speech recognizer from the above configuration information
        _recognizer = new SpeechRecognizer(_config, _sourceLanguageConfig, _audioConfig);

        // Subscribe to the Recognizing and Recognized events. As the user speaks individual
        // utterances, intermediate recognition results are sent to the Recognizing event,
        // and the final recognition results are sent to the Recognized event.
        _recognizer.Recognizing += (s, e) => HandleRecognizingEvent(e);
        _recognizer.Recognized += (s, e) => HandleRecognizedEvent(e);

        // Create a task completion source to wait for the session to stop. This is needed in
        // console apps to prevent the main thread from terminating while the recognition is
        // running asynchronously on a separate background thread.
        _sessionStoppedNoError = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe to SessionStarted and SessionStopped events. These events are useful for
        // logging the start and end of a speech recognition session. In console apps, this is
        // used to allow the application to block the main thread until recognition is complete.
        _recognizer.SessionStarted += (s, e) => HandleSessionStartedEvent(e);
        _recognizer.SessionStopped += (s, e) => HandleSessionStoppedEvent(e);

        // Subscribe to the Canceled event, which indicates that the recognition operation
        // was stopped/canceled, likely due to an error of some kind.
        _recognizer.Canceled += (s, e) => HandleCanceledEvent(e, _sessionStoppedNoError);

        _keywordModel = KeywordRecognitionModel.FromFile(keywordFileName);
    }

    public void OnRecognizing(Action<SpeechRecognitionEventArgs>? recognizing)
    {
        _recognizingEvent = recognizing;
    }

    public void OnRecognized(Action<SpeechRecognitionEventArgs>? recognized)
    {
        _recognizedEvent = recognized;
    }

    public async Task<bool> RecognizeAsync()
    {
        // Start keyword recognition
        await _recognizer.StartKeywordRecognitionAsync(_keywordModel);
        Console.WriteLine("Listening for keyword; press ENTER to stop ...\n");

        // Wait for the session to stop. The Task will not complete until the recognition
        // session stops, and the result will indicate whether the session completed
        // or was canceled.
        return await _sessionStoppedNoError.Task ? true : false;
    }

    private void HandleRecognizingEvent(SpeechRecognitionEventArgs e)
    {
        Console.WriteLine($"RECOGNIZING: {e.Result.Text}");
        _recognizingEvent?.Invoke(e);
    }

    private void HandleRecognizedEvent(SpeechRecognitionEventArgs e)
    {
        if (e.Result.Reason == ResultReason.RecognizedKeyword && !string.IsNullOrEmpty(e.Result.Text))
        {
            Console.WriteLine($"RECOGNIZED: {e.Result.Text}\n");
        }
        else if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
        {
            Console.WriteLine($"RECOGNIZED: {e.Result.Text}\n");
        }
        else if (e.Result.Reason == ResultReason.NoMatch)
        {
            Console.WriteLine($"NOMATCH: Speech could not be recognized.\n");
        }

        _recognizedEvent?.Invoke(e);
    }

    private static void HandleSessionStartedEvent(SessionEventArgs e)
    {
        Console.WriteLine($"SESSION STARTED: {e.SessionId}.\n");
    }

    private static void HandleSessionStoppedEvent(SessionEventArgs e)
    {
        Console.WriteLine($"SESSION STOPPED: {e.SessionId}.");
   }

    private static void HandleCanceledEvent(SpeechRecognitionCanceledEventArgs e, TaskCompletionSource<bool> sessionStoppedNoError)
    {
        Console.WriteLine($"CANCELED: Reason={e.Reason}");

        // Check the CancellationReason for more detailed information.
        if (e.Reason == CancellationReason.EndOfStream)
        {
            Console.WriteLine($"CANCELED: End of the audio stream was reached.");
        }
        else if (e.Reason == CancellationReason.Error)
        {
            Console.WriteLine($"CANCELED: ErrorCode={e.ErrorCode}");
            Console.WriteLine($"CANCELED: ErrorDetails={e.ErrorDetails}");
            Console.WriteLine($"CANCELED: Did you update the subscription info?");
        }

        // Set the task completion source result so the main thread can exit
        sessionStoppedNoError.TrySetResult(e.Reason != CancellationReason.Error);
    }

    private SourceLanguageConfig _sourceLanguageConfig;
    private SpeechConfig _config;
    private AudioConfig _audioConfig;
    private SpeechRecognizer _recognizer;
    private KeywordRecognitionModel _keywordModel;
    private TaskCompletionSource<bool> _sessionStoppedNoError;
    private Action<SpeechRecognitionEventArgs>? _recognizingEvent;
    private Action<SpeechRecognitionEventArgs>? _recognizedEvent;
}