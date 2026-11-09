# What is APMAIN
<sub>It's pretty easy to understand it's the "app"-"main" &mdash; MainApp.</sub>

> [!NOTE] 
> **WHAT YOU SHOULD KNOW**
>
> ApMain contains the REST API's of Mentos<sup>[1](mentos-whatis.md)</sup> that allows user level operation such as logging-in, tracking, ticketing, security and etc.
>
> It connects to [KeiNeokami](mailto:unknown.bit.609@gmail.com)'s tailnet, as a means of Transport Layer Security so that only the `HOST` and `DBSERV` are the ones communicating, without any worries to a [MITMATK](https://www.fortinet.com/resources/cyberglossary/man-in-the-middle-attack)'s 

# Getting started

## Prerequisites 
> NOTE: Please inform [KeiNeokami](mailto:unknown.bit.609@gmail.com) as he's the one hosting the tailnet as of the moment. <sub>(ps.s. Or you can *steal* and use his laptop either way works anyway)</sub>

Installation

### Windows

1. Download the official [Tailscale for Windows](https://tailscale.com/docs/install/windows) installer (`.exe`) from Tailscale.
2. Run the installer and accept the Windows administrator/UAC prompt for the official Tailscale application.
3. Open the Tailscale icon from the system tray and select **Log in**.
4. Sign in using the account invited to the existing PALs tailnet. Check that this is the intended tailnet; do not create a new tailnet.

### Linux

For mainstream Linux distributions supported by Tailscale's install script, run the commands from the [official Linux installation guide](https://tailscale.com/docs/install/linux):

```bash
curl -fsSL https://tailscale.com/install.sh | sh
sudo tailscale up
```

`tailscale up` prints a sign-in URL. Open it in a browser and authenticate with the account invited to the existing PALs tailnet.

### What to accept

1. Accept the operating-system administrator prompt only for the official Tailscale installer or service. Linux `sudo` is needed to install/start the network service.
2. Sign in with the tailnet account invited by the owner. Do not create a separate tailnet if asked to join or set one up.
3. If prompted to join a tailnet, verify the invitation came from the owner and is for the PALs tailnet before accepting. A device-share invite is not the same as joining the tailnet.
4. If the laptop requires device approval, ask the tailnet owner/admin to approve it; do not bypass approval.
5. Tailnet membership does not automatically grant access to every service. The owner/admin must ensure the access policy permits this HOST laptop to reach the DBSERV API on apmain.
6. Decline unrelated access, administrator, or sharing prompts unless the tailnet owner confirms they are needed.

After signing in, check the Tailscale tray status on Windows or run `tailscale status` on Linux. Joining the tailnet does not by itself enable Funnel, configure HTTPS, or publish the Mentos API.

<sub>References: [Tailscale quickstart](https://tailscale.com/docs/how-to/quickstart), [Windows installation](https://tailscale.com/docs/install/windows), [Linux installation](https://tailscale.com/docs/install/linux), and [access control](https://tailscale.com/docs/features/access-control).</sub>


