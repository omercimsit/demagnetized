using NUnit.Framework;

// testing the event bus to make sure subscribe/fire/unsubscribe actually works
// had a bug once where events were firing twice because I forgot to unsubscribe in OnDestroy
[TestFixture]
public class GameEventsTests
{
    [SetUp]
    public void SetUp()
    {
        // clear everything between tests so they don't affect each other
        GameEvents.ClearAllEvents();
    }

    [Test]
    public void OnClonePhaseChanged_FiresWithCorrectPhase()
    {
        int receivedPhase = -1;
        GameEvents.OnClonePhaseChanged += (phase) => receivedPhase = phase;

        GameEvents.InvokeClonePhaseChanged(2); // 2 = Rewinding

        Assert.AreEqual(2, receivedPhase);
    }

    [Test]
    public void OnRecordingStarted_MultipleListeners()
    {
        int count = 0;
        GameEvents.OnRecordingStarted += () => count++;
        GameEvents.OnRecordingStarted += () => count++;

        GameEvents.InvokeRecordingStarted();

        Assert.AreEqual(2, count);
    }

    [Test]
    public void Unsubscribe_StopsReceivingEvents()
    {
        int count = 0;
        System.Action handler = () => count++;

        GameEvents.OnRecordingStarted += handler;
        GameEvents.InvokeRecordingStarted();
        GameEvents.OnRecordingStarted -= handler;
        GameEvents.InvokeRecordingStarted();

        Assert.AreEqual(1, count);
    }

    [Test]
    public void OnGamePaused_PassesBoolCorrectly()
    {
        bool wasPaused = false;
        GameEvents.OnGamePaused += (isPaused) => wasPaused = isPaused;

        GameEvents.InvokeGamePaused(true);
        Assert.IsTrue(wasPaused);

        GameEvents.InvokeGamePaused(false);
        Assert.IsFalse(wasPaused);
    }

    [Test]
    public void OnPlaybackStarted_FiresCorrectly()
    {
        bool fired = false;
        GameEvents.OnPlaybackStarted += () => fired = true;

        GameEvents.InvokePlaybackStarted();

        Assert.IsTrue(fired);
    }

    [Test]
    public void ClearAllEvents_RemovesAllListeners()
    {
        int count = 0;
        GameEvents.OnClonePhaseChanged += (_) => count++;
        GameEvents.OnRecordingStarted += () => count++;
        GameEvents.OnPlayerDied += () => count++;

        GameEvents.ClearAllEvents();
        GameEvents.InvokeClonePhaseChanged(1);
        GameEvents.InvokeRecordingStarted();
        GameEvents.InvokePlayerDied();

        Assert.AreEqual(0, count);
    }

    [Test]
    public void Fire_WithNoListeners_DoesNotThrow()
    {
        // make sure events don't crash when nobody is listening
        Assert.DoesNotThrow(() => GameEvents.InvokeClonePhaseChanged(0));
        Assert.DoesNotThrow(() => GameEvents.InvokeRecordingStarted());
        Assert.DoesNotThrow(() => GameEvents.InvokePlaybackEnded());
        Assert.DoesNotThrow(() => GameEvents.InvokePlayerDied());
    }

    [Test]
    public void OnDLSSModeChanged_PassesModeIndex()
    {
        int receivedMode = -1;
        GameEvents.OnDLSSModeChanged += (mode) => receivedMode = mode;

        GameEvents.InvokeDLSSModeChanged(3); // 3 = Balanced

        Assert.AreEqual(3, receivedMode);
    }

    [Test]
    public void FullRecordingCycle_EventOrder()
    {
        // simulate a full clone recording cycle and check events fire in order
        var eventLog = new System.Collections.Generic.List<string>();
        GameEvents.OnClonePhaseChanged += (p) => eventLog.Add("phase_" + p);
        GameEvents.OnRecordingStarted += () => eventLog.Add("rec_start");
        GameEvents.OnRecordingStopped += () => eventLog.Add("rec_stop");
        GameEvents.OnPlaybackStarted += () => eventLog.Add("play_start");
        GameEvents.OnPlaybackEnded += () => eventLog.Add("play_end");

        GameEvents.InvokeClonePhaseChanged(1);
        GameEvents.InvokeRecordingStarted();
        GameEvents.InvokeRecordingStopped();
        GameEvents.InvokeClonePhaseChanged(4);
        GameEvents.InvokePlaybackStarted();
        GameEvents.InvokePlaybackEnded();
        GameEvents.InvokeClonePhaseChanged(0);

        Assert.AreEqual(7, eventLog.Count);
        Assert.AreEqual("phase_1", eventLog[0]);
        Assert.AreEqual("rec_start", eventLog[1]);
        Assert.AreEqual("rec_stop", eventLog[2]);
        Assert.AreEqual("phase_4", eventLog[3]);
        Assert.AreEqual("play_start", eventLog[4]);
        Assert.AreEqual("play_end", eventLog[5]);
        Assert.AreEqual("phase_0", eventLog[6]);
    }
}
