# Testing accuracy

The accuracy test checks how close the bubble lands to where you look with the calibration in use. Open it with **Test accuracy** in the calibrations window, or `make run ARGS="--accuracy-test"`. Begin and close by looking right or left, like the other screens. On the results, close your eyes for 1.5 s first, so looking at the dots isn't taken as an answer.

- dots show up one by one: 15 sitting normally (5 columns by 3 rows, so points between the calibration's 3 columns are tested too), then 3 leaning left and 3 leaning right (left, middle, right), and 3 with the head tilted up and 3 tilted down (top, middle, bottom). Leaning and tilting are guided like in the calibration, measured against how you sat in the first part. About a minute and a half
- each dot: 0.8 s for the eyes to get there, then 1.5 s where the tracker's bubble position is recorded
- the bubble isn't shown while it runs, otherwise you'd follow it instead of the dot
- at the end every dot is drawn as a ring, with a line to a filled dot where the bubble landed (median of its frames). Yellow sitting normally, blue leaning left, pink leaning right, green head tilted up, orange head tilted down. Above it: how far off left/right and up/down it was per way of sitting (`meanErrorX`, `meanErrorY`), which side it leans to, and the left/right error per column

Saved in `app/Calibration/Saved/tests/<date>.json` (one line), with the report and every frame of every dot (same frames as a recording, plus `estimateX` / `estimateY`, where the bubble was). That's what to look at to decide what to improve: whether more calibration points would help, whether the leaning correction holds up, whether one side needs its own fit. The numbers are in `app/Calibration/Accuracy/AccuracyTest.cs`.
