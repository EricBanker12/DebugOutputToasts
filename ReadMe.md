# Debug Output Toasts
This is a simple WPF application which captures Kernel32.dll OutputDebugString messages, and allows conditional modification and forwarding of messages to Windows 10 Toast notifications.

## Settings
* Notification Settings
  * Show Notifications - Show Toast notifications on receiving a message.
  * Play Sound - Play the default sound on notification.
  * Throttle - After queuing a toast notification, skip following notifications for a short duration.
  * Debounce - Wait before queuing a toast notification. Skip the preceding notifications if a following message is given.
* Filter Settings
  * A/a - Match the capitalization of the input.
  * Rx - Use Regualr Expression for pattern matching.
  * Inclusion Filters - Show only messages which match at least one of these filters
  * Exclusion Filters - Show only messages which match none of these filters
  * Replacement Filters - Replace part of a message with a given replacement.

## Images

**Notification Settings**  
This configuration will silently display Toast notifications, skipping following notifications for 5 seconds.  
![](https://raw.githubusercontent.com/EricBanker12/DebugOutputToasts/master/Images/Settings.png)

**Toast Notification**  
![](https://raw.githubusercontent.com/EricBanker12/DebugOutputToasts/master/Images/Notification.png)

**Inclusion Filter**  
This filter forces only messages containing 'hello' (case-insensitive) to be shown or notified. 
![](https://raw.githubusercontent.com/EricBanker12/DebugOutputToasts/master/Images/Inclusion.png)

**Exclusion Filter**  
This filter forces messages containing 'Hello' (case-sensitive) to be hidden.  
![](https://raw.githubusercontent.com/EricBanker12/DebugOutputToasts/master/Images/Exclusion.png)

**Replacement Filter**  
This filter uses Regular Expression to replace parts of a matching message with new content.  
![](https://raw.githubusercontent.com/EricBanker12/DebugOutputToasts/master/Images/Replace.png)