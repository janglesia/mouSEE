# Answering by looking

Every question in the calibration can be answered without hands: the startup question, the eye check's Begin and its result (continue or do it again), and the grid screen's Begin, its result (try it out or do the grid again) and its preview (keep it or do the grid again).

- **look to the right** for yes, **to the left** for no, and hold it for 3 seconds. The bar on that side fills up while you hold it
- it starts counting only after your eyes have been in the middle once, so you can't answer a question before you've read it, and after an answer they have to come back to the middle before the next one
- a blink while holding doesn't start the count over
- it's the eyes that count, not head turns. Turning your head to the right while keeping your eyes on the screen turns your eyes to the left in their sockets, so head turns could read a yes as a no. Look with your eyes, past the edge of the screen is fine
- it uses the tracker's gaze direction (`eyes.gaze.h`, the same "left" / "right" as in the overlay's readout): more than 0.12 off center to start, it stays on that side down to 0.08 so it doesn't flicker
- while the steps or the grid targets run it's off, looking around is part of those
- **on screens you look around on first** (the grid's preview, the accuracy test's results with the fine-tune) looking at a dot near the edge was taken as an answer. There, left and right only count after you **close your eyes for 1.5 s** ("I'm done looking"): the No / Yes sides are dimmed and a yellow bar in the middle fills while your eyes are closed. Then a sound, the question is read out (not on the accuracy test's results, nothing is said there), and you look right or left as usual. A blink (~0.3 s) or one eye closed doesn't count, an eye reading open for a frame or two doesn't start it over

## The middle dot

Whenever a screen asks you to look at the middle, there's a dot there: the eye check's two head tilt steps, the re-center, and a look left / right question that's waiting for your eyes to come to the middle first (only if they weren't there within 0.4 s, otherwise it would flash up every time). Not on the grid screen: the logo is what you look at there, and a second circle in the middle during and after the 20 points only got in the way. It's in the calibration's own colours: the calibration yellow on the dark screens, and the same dark as those screens when the background is the yellow (the eye check's check round). Whichever of the two stands out more from the background behind it, so it changes when the eye check turns yellow (`UiColors.MiddleDot`, app/UI/UiColors.cs). The dot is `MiddleDot` (app/UI/MiddleDot.xaml).

`LookChoice` (app/LookToAnswer) is the logic, `ChoicePrompt` (app/LookToAnswer) shows it and can be put in any window: `Choice.Ask("Begin", "Cancel")` and handle `Choice.Chosen`. For a screen you look around on, `Choice.AskAfterLooking(...)` waits for the closed eyes first (`ClosedEyesGate`, app/LookToAnswer) and fires `Choice.Ready` when they were.
