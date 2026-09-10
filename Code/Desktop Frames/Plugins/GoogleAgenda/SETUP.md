# Setting up the Agenda plugin

The Agenda frame reads and writes Google Calendar and Google Tasks. To do that it
needs a **Google client**: a small JSON file, created in Google Cloud Console, that
identifies this program to Google.

The file is not shipped with the program and never goes into the repository. It
lives in the profile, at `Profiles/<profile>/GoogleAgenda/google_client.json`, and
the sign-in token next to it is encrypted for the current Windows user.

For a program installed on somebody's computer the client is not a password — Google
says as much about installed applications, since anything on the disk can be read.
It is kept out of the repository so that nobody spends the quota of another person's
project, not because reading it would give access to a calendar.

## Creating the client (about ten minutes, once)

1. Open [Google Cloud Console](https://console.cloud.google.com/) and create a
   project.
2. Under **APIs & Services → Library**, enable **Google Calendar API** and
   **Google Tasks API**.
3. Under **APIs & Services → OAuth consent screen**, choose **External**. Fill in
   the required fields, then under **Test users** add the Google account you will
   sign in with.
4. Under **APIs & Services → Credentials**, choose **Create credentials → OAuth
   client ID**, with application type **Desktop app**. Download the JSON.

The type matters. A **Web application** client looks almost identical, is refused
by the settings window, and would otherwise fail at sign-in with an error about
redirect addresses.

## Using it

1. Create a frame of type **Agenda**.
2. Open its settings and choose **Choose Google client file…**. Pick the JSON.
3. Choose **Sign in**. A browser page asks for consent; after that the frame fills
   in, and stays signed in across restarts.

## Things worth knowing

- **Testing mode signs you out weekly.** While the consent screen is in *Testing*,
  Google expires the sign-in after seven days for calendar access, and the frame
  will ask again. Publishing the consent screen removes that, but calendar access
  is a sensitive scope, so publishing means going through Google's verification.
- **Only accounts listed as test users can sign in** while in Testing. Somebody
  else using your client needs to be added to that list, or to make their own.
- **A task's time of day is not in the Tasks API.** Google keeps it on a calendar
  entry standing in for the task; the plugin reads both and puts them back together.
  A task created from the frame gets a date, not an hour, because the API would drop
  the hour silently.
