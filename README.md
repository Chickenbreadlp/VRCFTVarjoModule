# VRCFaceTracking Varjo module

The module provides gaze tracking data for VRCFaceTracking tool while using Varjo headsets.

## Installation

0. Download [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) tool
1. Download the latest version within the Module Registry. [Docs Reference](https://docs.vrcft.io/docs/hardware/varjo)
2. Enjoy ^^

## Config
When the module get's loaded it will create a Config.ini file in the module directory as well as add a link to it on the desktop.  
With this Config.ini you can control how the module handles certain aspects of the Varjo Eye Tracking.  
All the different options are also documented in the default created `.ini` file, but if you lost those or need a more thorough documentation, read on.  

These are the settings you can adjust and what they do: (all inputs are case insensitive!)

### CreateLink  

__Valid Inputs:__
- True *(default)*
- False

This one is pretty self explanetory. When this setting is true, it will create a shortcut to the config file on the desktop (if the shortcut doesn't exist).

### DoubleTime

__Valid Inputs:__
- True
- False *(default)*

Normally the module runs at 100Hz and configures the Varjo SDK to only provide that data at 100Hz.
However with this setting enabled, the module will run at 200Hz and configure the SDK to also provide ET data at 200Hz.  

**NOTE:** This option will do *nothing* but increase CPU usage for older Varjo headsets, as these were only equipped with 100Hz ET cameras.  
(older Varjo headsets meaning VR-1, VR-2, VR-2 Pro & XR-1)

### PickyTracking

__Valid Inputs:__
- True *(default)*
- False

This option will change how confident the Varjo SDK has to be in gaze tracking points for the Module to consider using them.  

True means that the Module will only consider tracking points that the Varjo SDK has a very high confidence in. This option is recommended, as it reduces the amount of random eye jittering.
However it does mean that the Eye gaze can more often just stop tracking, when the SDK has a low confidence overall in tracking your eyes.  

False on the other hand allows the module to also consider "Compensated" gaze tracking points as usable. This will increase the amount of random eye gaze jittering, but can solve problems like the eyes getting stuck often.  

**NOTE:** Older versions of the module handled this setting as-if it was was set to false, however the default now changed to true! Please be aware of this when upgrading this module to 4.1.0 (or downgrading to an older version)

### StabalizingCycles

__Valid Inputs:__
- Any *positive* Integer number
- (default: 1)

This module will pause Gaze tracking for the number of cycles set in this option whenever the gaze status goes from an ignored status to one tracked by the module.  
1 cycle is 10ms when the `DoubleTime` setting is `false` and it's 5ms when the `DoubleTime` setting is `true`.  
Please be cautios when messing with this setting as it can make your eye tracking visible pause if you set it too high.

**NOTE:** This setting also applies to eye openness when `EyeLidStrat` is set to `RestrictedSpeed`

### UntrackedEyeFollowTracked

__Valid Inputs:__
- True *(default)*
- False

Normally when one eye looses gaze tracking but the other is still tracked, you'd get a sort-off gecko-eye effect, with one eye moving and the other not.  
With this setting set to true, the module will tweak the gaze position of the non-tracked eye based on movement from the tracked eye.  
The gaze of the untracked eye might not be 100% accurate when enabled, but it does solve the gecko-eye issue. Turn off only if you know what you're doing!

### EyeLidStrat

__Valid Inputs:__
- Bool
- Stepped
- RawFloat
- RestrictedSpeed *(default)*
- Hybrid

With this setting you control how the module should handle the eye openness values.  

- Bool & Stepped use the Eye gaze status to determine whether the eye should be shown open or close.  
  While Bool only has Open & Close, Stepped uses the four different gaze statuses to simulate a sort-off 4-step float eye lid.  
  **NOTE:** These two are the only options working correcting in VB 3.6.x and older, as proper float Eye Lids weren#t added until VB 3.7  
- RawFloat, as the name suggest will pass through the raw ET float value as given by the VB SDK (although parsing for Widen/Squeeze still happens)  
- RestrictedSpeed attempts to restrict the speed at which the eye lids can open whenever the module stops tracking the gaze of an eye.  
  This is supposed to assist with the eyelids sometimes randomly shooting open, but it might not work good enough for everyone.  
- Hybrid will force-close the eyelids based on the gaze status to a certain point but works otherwise like the RawFloat parameter. Hence the "Hybrid" name.
  To give an example, when the SDK reads that it cannot see the eyes, this option will force the eyes to at most 25% open.
  It's another approach to preventing the afforementioned eyes shooting open issue, but it has more potential to leave the eyes somewhat open.

### SqueezeThreshold

__Valid Inputs:__
- Any *positive* floating point number between 0.0 and 1.0
- (defaults: 0.15)

This option detemines at which point from the Varjo SDK eye openness float does the module split it up to emulate the SRanipal "Squeeze" value.  
This number cannot be larger then what's set for `WidenThreshold`, and to turn off parsing for this value entirely, set it to 0.

### WidenThreshold

__Valid Inputs:__
- Any *positive* floating point number between 0.0 and 1.0
- (defaults: 0.9)

This option detemines at which point from the Varjo SDK eye openness float does the module split it up to emulate the SRanipal "Widen" value.  
This number cannot be smaller then what's set for `SqueezeThreshold`, and to turn off parsing for this value entirely, set it to 1.

### MaxOpenSpeed

__Valid Inputs:__
- Any *positive* floating point number between 0.0 and 1.0
- (defaults: 0.1)

*This option only applies when `EyeLidStrat` is set to `RestrictedSpeed`*
This option set's how much a new eye float value may differ from the previous value for the module to consider it when the eye gaze is not tracked for that eye.  
When setting this value to 0.0 you effectively make it so the eyelid cannot open even a sliver as long as module doesn't track the gaze and setting it to 1.0 effectively turns the `EyeLidStrat` setting `RestrictedSpeed` into a less soptimized `RawFloat`
