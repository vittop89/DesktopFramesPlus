using Google.Apis.Auth.OAuth2;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>What the agenda is currently able to do.</summary>
    public enum AgendaState
    {
        /// <summary>No Google client on this installation.</summary>
        NotConfigured,
        /// <summary>A client is there, nobody has signed in.</summary>
        SignedOut,
        /// <summary>A consent page is open and waiting for a person.</summary>
        SigningIn,
        /// <summary>Signed in; nothing has been asked for yet.</summary>
        SignedIn,
        /// <summary>Something went wrong; <see cref="AgendaSession.LastError"/> says what.</summary>
        Failed
    }

    /// <summary>
    /// The signed-in session, and the single place that decides what state it is in.
    ///
    /// It sits between the frame and <see cref="CalendarAuth"/> so that neither the
    /// frame nor the settings window has to know how signing in works, and - more
    /// importantly - so that both of them see the same answer. Two windows asking the
    /// same question separately is how one ends up showing "signed in" while the
    /// other still offers the button.
    ///
    /// Every change is announced on <see cref="Changed"/>, already on the interface
    /// thread, so whoever listens can redraw without checking where it came from.
    /// </summary>
    public class AgendaSession
    {
        private CancellationTokenSource? _signInCancellation;

        /// <summary>
        /// The sessions in use, one for each profile.
        ///
        /// Shared rather than made per frame, because the thing being shared is not a
        /// saving: a second frame holding a second session sits on "signed out" while
        /// the first one draws the calendar, and no amount of redrawing reconciles
        /// them - they are answering from different state. One profile is one account,
        /// so one profile is one session.
        /// </summary>
        private static readonly Dictionary<string, AgendaSession> Shared =
            new Dictionary<string, AgendaSession>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The session for the profile now open, made once and then reused.</summary>
        public static AgendaSession ForCurrentProfile()
        {
            string profile = ProfileManager.CurrentProfileDir;

            lock (Shared)
            {
                if (!Shared.TryGetValue(profile, out AgendaSession? existing))
                {
                    existing = new AgendaSession();
                    Shared[profile] = existing;
                }

                return existing;
            }
        }

        private readonly object _gate = new object();

        /// <summary>The resume already under way, or already finished.</summary>
        private Task? _resume;

        public AgendaState State { get; private set; } = AgendaState.NotConfigured;

        public UserCredential? Credential { get; private set; }

        /// <summary>Set only alongside <see cref="AgendaState.Failed"/>.</summary>
        public string? LastError { get; private set; }

        /// <summary>Raised on the interface thread whenever the state changes.</summary>
        public event Action? Changed;

        /// <summary>
        /// Picks up a session granted earlier, without ever opening a browser.
        ///
        /// Called while the frame is being built, so it must be able to answer
        /// "nothing to resume" quietly. A program that greets somebody with a consent
        /// page because their computer just started has misunderstood what consent is.
        /// </summary>
        public Task ResumeAsync()
        {
            // Every frame asks, because no frame can draw before it knows the answer -
            // but it is one answer, and reading the stored token twice over would race
            // two sign-in states against each other for no gain.
            lock (_gate)
            {
                return _resume ??= ResumeOnceAsync();
            }
        }

        /// <summary>
        /// Looks again, after the client has been added or replaced.
        ///
        /// <see cref="ResumeAsync"/> answers once and keeps that answer, which is right
        /// for frames asking at start-up and wrong here: its answer was "not set up",
        /// and that has just stopped being true.
        /// </summary>
        public Task RecheckAsync()
        {
            lock (_gate)
            {
                _resume = ResumeOnceAsync();
                return _resume;
            }
        }

        private async Task ResumeOnceAsync()
        {
            if (!AgendaCredentials.AreAvailable())
            {
                Move(AgendaState.NotConfigured);
                return;
            }

            UserCredential? existing = await CalendarAuth.TryResumeAsync(CancellationToken.None)
                                                         .ConfigureAwait(false);

            Credential = existing;
            Move(existing != null ? AgendaState.SignedIn : AgendaState.SignedOut);
        }

        /// <summary>
        /// Opens the browser and waits for consent.
        ///
        /// The waiting happens off the interface thread: the call blocks until the
        /// person finishes, gives up, or closes the tab, and a frame that froze
        /// meanwhile would look like the program had crashed.
        /// </summary>
        public async Task SignInAsync()
        {
            if (State == AgendaState.SigningIn) return;

            if (!AgendaCredentials.AreAvailable())
            {
                Fail(Localization.Strings.AgendaNotConfigured);
                return;
            }

            _signInCancellation?.Cancel();
            _signInCancellation = new CancellationTokenSource();
            CancellationToken token = _signInCancellation.Token;

            Move(AgendaState.SigningIn);

            try
            {
                UserCredential granted = await Task.Run(() => CalendarAuth.SignInAsync(token), token)
                                                   .ConfigureAwait(false);

                Credential = granted;
                Move(AgendaState.SignedIn);

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    "GoogleAgenda: signed in.");
            }
            catch (OperationCanceledException)
            {
                // Closing the consent tab is an answer, and the answer is no. Back to
                // where we were, without an error nobody needs to dismiss.
                Move(AgendaState.SignedOut);
            }
            catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException ex)
                when (string.Equals(ex.Error?.Error, "access_denied", StringComparison.Ordinal))
            {
                // Google said no. The commonest reason by far is an account that is not
                // on the client's list of test users, and Google says so only in the
                // browser tab, which is gone by the time anyone looks back here.
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    "GoogleAgenda: Google did not grant access (access_denied).");

                Fail(Localization.Strings.AgendaAccessDenied);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: sign-in failed: {ex.Message}");

                Fail(Localization.Strings.AgendaSignInFailed);
            }
        }

        /// <summary>Gives the grant back to Google and forgets it here.</summary>
        public async Task SignOutAsync()
        {
            _signInCancellation?.Cancel();

            UserCredential? previous = Credential;
            Credential = null;

            try
            {
                await CalendarAuth.SignOutAsync(previous, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                // Whatever Google answered, this installation is signed out: the local
                // token is gone either way, so saying anything else would be a lie the
                // next request would expose.
                Move(AgendaCredentials.AreAvailable()
                    ? AgendaState.SignedOut
                    : AgendaState.NotConfigured);
            }
        }

        /// <summary>Stops a sign-in still waiting on a browser tab nobody came back to.</summary>
        public void Cancel()
        {
            _signInCancellation?.Cancel();
        }

        private void Fail(string message)
        {
            LastError = message;
            Move(AgendaState.Failed);
        }

        private void Move(AgendaState next)
        {
            if (next != AgendaState.Failed) LastError = null;

            State = next;

            // Announced on the interface thread because every listener redraws, and
            // sign-in answers on a background one.
            Application.Current?.Dispatcher.InvokeAsync(() => Changed?.Invoke());
        }
    }
}
