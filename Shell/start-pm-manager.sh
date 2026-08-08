#!/bin/bash

# Deactivate any active virtual environment
if [ -n "$VIRTUAL_ENV" ]; then
    deactivate
fi

# Activate the streamlit-env virtual environment
source ~/streamlit-env/bin/activate

# Navigate to the app folder and run the app
streamlit run pm-manager.py
