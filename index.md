**NOTE: THIS TOOL IS CURRENTLY IN DEVELOPMENT AND IN BETA STATUS!**

**If you're happy helping to test and report issues on incomplete and buggy software, give it a go. If you're not, and expect something perfect and polished, you'll want to wait until it's a little more stable and complete.**

**Please don’t download it then discover it’s not complete enough for you and give it a bad rating!**

# What is it?

Better Bravo Lights (BBL) aims to be a 'better' replacement for the Aerosoft Honeycomb Bravo Throttle lights tool (aka the "AFC bridge").

Note that Better Bravo Lights is completely standalone and does not require the original Honeycomb Bravo lights driver.

BBL provides a number of significant improvements over the original Aerosoft/Honeycomb Bravo Lights driver:

- It supports different configurations for different aircraft; this is critical because oil pressures, fuel pressures and battery voltages vary wildly from aircraft to aircraft. A configuration that says "Light the 'LOW OIL PRESSURE' light if the oil pressure is less than 30 psi" is useless on an aircraft whose oil presure never rises above 20 psi. So it's critical to have aircraft-specific configurations. BBL supports this. In contrast the AFC Bridge supports only _one_ configuration across all aircraft.
- Lights respond immediately. This is important when using autopilot buttons: with immediate updates, if an autopilot button doesn't light up when you press it, it's because the autopilot is rejecting that mode request, not because the lights tool is waiting a second or two before lighting up the button. If you use an aircraft such as the Hawk T1 which has flashing warning lights, the Bravo lights will flash along with them. The AFC Bridge only updates lights every second or so, so feels extremely sluggish in comparison.
- It comes with a complete set of configurations for all of the aircraft that come with MSFS.
- It supports both A: variables and L: variables. Putting the technojargon to one side, if you want to make the MASTER WARNING and MASTER CAUTION lights work, you need a tool that can read L: variables from MSFS. AFC Bridge cannot support L: variables.

If you need to change the BBL configuration - for instance, to add entries for a new aircraft - BBL offers significant advantages over AFC Bridge:

- Changes to the configuration file are applied within a second; AFC Bridge requires a flight restart, which is problematic if you're modifying the configuration based on a scenario that's non-trivial to establish.
- It provides a dedicated 'debugger' user-interface to help with the development of conditions, showing condition expressions for each light and a real-time view of the relevant simulator variables.
- It supports more sophisticated conditions (specifically, any combinations of variables, constants and arithmetic).
- The configuration file is a fraction of the size of the AFC Bridge configuration file, and much more readable.

# What's the status of it?

- With a fair following wind, it all works fine; I've been using it day-to-day for some time.
- Error-handling is minimal right now so you may not get good feedback if you get the configuration wrong.
- It may not yet be able to find all possible installation locations for all varieties of Windows Store and Steam installations. It's safe: if it can't figure things out, it won't change anything.

In summary, it won't break anything - so it's safe to try it out - but it may not complete or bulletproof just yet.

# How do I install/uninstall it?

Simply:

1. download it
1. unpack the zip (anywhere you like: it does _not_ need to go in the Community folder)
1. run `install.bat`

The installation process will modify your MSFS startup so that it no longer runs the Honeycomb AFC Bridge but instead runs Better Bravo Lights.

To uninstall, just run `uninstall.bat`; that'll remove Better Bravo Lights from your MSFS startup and restore the Honeycomb AFC Bridge (if you have it).

Note: Better Bravo Lights v0.6.0+ _no longer requires or uses FSUIPC_. It now comes with its own WASM module for reading L: variables.

# How do I configure it?

Firstly: you may not need to. The configuration that comes with it has specific configurations for every aircraft that comes with all versions of MSFS, and a few more.

But if you want to change the configuration for existing aircraft or to add configuration for other aircraft, there's a [detailed configuration page](./configuration.md) to consult.

# FAQ

## Will this mess up my existing Bravo configuration?

No. It doesn't change your existing Bravo configuration; it's a completely new Bravo lights tool written from scratch. Installing it will change your MSFS startup configuration to run Better Bravo Lights instead of the Honeycomb AFC Bridge, but that's the only change, and that's reversible.

## What features are missing?

There's a limit on what features can realistically be added to a tool which simply turns lights on and off, but here are some things coming soon:

- better error reporting for invalid expressions; e.g. if you forget the units for an A: variable the message isn't very helpful
- a UI for showing all of the L: variables available in the simulator

## What bugs are there?

1. Virtually no nice error-checking and reporting.
1. The debugger UI is messy. I’ll tidy it up when everything else is stable. Right now, it works, so making it prettier is lower priority.
1. When returning to the main menu, the lights will turn on and off a few times and then remain on in a strange state. This is because the simulator tells us that it’s not running, then it is, then it isn’t, and then it tells us it’s running whilst on the main menu. [If you’re technical and know how to detect that we’re at the main menu and not in the sim, please let me know!]

## How do I report problems or make suggestions?

If something doesn't work properly, you find problems with the configuration, you can't figure out how to install it, or need anything else, please raise an issue at the GitHub repository: [https://github.com/RoystonS/BetterBravoLights/issues](https://github.com/RoystonS/BetterBravoLights/issues)

## Can I see the code?

Of course. It's all available on GitHub as MIT-licensed Open Source: [https://github.com/RoystonS/BetterBravoLights](https://github.com/RoystonS/BetterBravoLights)

### Third party software

Better Bravo Lights makes use of some _excellent_ third party libraries, without which it would have been too painful to create. Thank you all!

---

#### HidSharp 2.1.0

This library enables Better Bravo Lights to output the USB signals needed to drive the Bravo.

https://www.zer7.com/files/oss/hidsharp/LICENSE.txt

HIDSharp is Copyright 2010-2019 James F. Bellinger <http://www.zer7.com/software/hidsharp>

Licensed under the Apache License, Version 2.0

---

#### sly 2.7.0.3

This library helps Better Bravo Lights to construct and handle its variable expression syntax.

https://github.com/b3b00/csly/blob/a7b9031342d051aa7bc76e92658ccc6e3b6d921f/LICENSE

sly is Copyright (c) 2017 Olivier Duhart

Licensed under the MIT License

---

Full details of _all_ these licences can be found in the `LICENSES.md` which is included in the software distribution, or in the GitHub repo at https://github.com/RoystonS/BetterBravoLights/blob/main/BravoLights/LICENSES.md.