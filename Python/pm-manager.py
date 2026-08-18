import os
import sys

import pandas as pd
import requests
import streamlit as st

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from api_config import MODELS_ENDPOINT

st.set_page_config(page_title="Predictive Model Manager", layout="wide")
st.title("Predictive Model Manager")

tab_home, tab_models = st.tabs(["Home", "Models"])

with tab_home:
    pass

with tab_models:
    try:
        response = requests.get(MODELS_ENDPOINT)
        response.raise_for_status()
        models = response.json()
    except requests.RequestException as e:
        st.error(f"Failed to load models from {MODELS_ENDPOINT}: {e}")
    else:
        df = pd.DataFrame(models).drop(columns=[
            "id", "modelFeatures", "trainingStart", "trainingEnd",
            "windowStart", "windowEnd", "featureLocation", "outputLocation",
            "schemaName", "tableName",
        ])

        st.dataframe(df, width="stretch", hide_index=True)
