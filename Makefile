# Project tasks. Run from the project folder, e.g.  make setup
# Needs GNU make from MSYS2 (see README). Run "make" on its own to list tasks.

VENV      := tracker/.venv
PY        := $(VENV)/Scripts/python.exe
INSTALLED := $(VENV)/installed.stamp
MODEL     := tracker/face_landmarker.task
MODEL_URL := https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task

# Extra options for the tracker, e.g.  make run ARGS="--camera 1"
ARGS ?=

# Resolution shortcut, e.g.  make run 720  or  make tracker 360
# Make sees "720" as a second target, so pick it out of the command line here.
RES       := $(filter 360 480 720 1080,$(MAKECMDGOALS))
RES_360   := --width 640 --height 360
RES_480   := --width 640 --height 480
RES_720   := --width 1280 --height 720
RES_1080  := --width 1920 --height 1080
RES_ARGS  := $(RES_$(RES))

.PHONY: help setup build run tracker clean reset 360 480 720 1080

help:
	@echo "make setup     create .venv, install packages, download model, build app"
	@echo "make build     build the C# app"
	@echo "make run       run the app with the camera window"
	@echo "make tracker   run the tracker on its own with the camera window"
	@echo "               (for both: add 360, 480, 720 or 1080 to pick a resolution, e.g. make run 720,"
	@echo "                and pass other tracker options with ARGS, e.g. make run ARGS=\"--camera 1\")"
	@echo "make clean     delete build output (app/bin, app/obj, __pycache__)"
	@echo "make reset     delete everything generated and set it all up again"

setup: $(INSTALLED) $(MODEL) build

# Python environment. Uses the py launcher so it picks 3.12 even if other versions are installed.
$(PY):
	py -3.12 -m venv $(VENV)

# Reinstalls packages only when requirements.txt changes
$(INSTALLED): tracker/requirements.txt | $(PY)
	$(PY) -m pip install --upgrade pip
	$(PY) -m pip install -r tracker/requirements.txt
	touch $@

$(MODEL):
	curl -L --fail -o $@ $(MODEL_URL)

build:
	cd app && dotnet build

run: setup
	cd app && dotnet run -- --preview $(RES_ARGS) $(ARGS)

tracker: $(INSTALLED) $(MODEL)
	cd tracker && .venv/Scripts/python.exe face_tracker.py --preview $(RES_ARGS) $(ARGS)

# Do nothing: these only exist so "make run 720" doesn't fail with "no rule to make target 720"
360 480 720 1080:
	@:

clean:
	rm -rf app/bin app/obj tracker/__pycache__

reset: clean
	rm -rf $(VENV) $(MODEL)
	$(MAKE) setup
