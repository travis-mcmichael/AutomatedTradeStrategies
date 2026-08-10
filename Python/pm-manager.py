import os
import sys

import pandas as pd
import requests
import streamlit as st

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from api_config import MODELS_ENDPOINT

st.set_page_config(page_title="Predictive Model Manager", layout="wide")
st.title("Predictive Model Manager")

TARGET_OPTIONS = ["HIGH", "LOW", "CLASS"]
BAR_TYPE_OPTIONS = ["session", "window", "minute"]
SOURCE_OPTIONS = ["daily", "prevMinute", "currMinute"]

MODELS_GRID_KEY = "models_grid"


def source_to_display(source):
    return source if source in ("prevMinute", "currMinute") else "daily"


def display_to_source(source):
    return None if source == "daily" else source


def build_features_df(model_features):
    rows = [
        {"Name": f.get("name", ""), "Source": source_to_display(f.get("source"))}
        for f in (model_features or [])
    ]
    return pd.DataFrame(rows, columns=["Name", "Source"])


def build_model_features_payload(features_df, original_model_features):
    original_params_by_name = {
        f.get("name", ""): f.get("modelFeatureParams") or []
        for f in (original_model_features or [])
    }

    features = []
    for row in features_df.to_dict("records"):
        name = row["Name"]
        if not name:
            continue
        features.append({
            "name": name,
            "source": display_to_source(row["Source"]),
            "modelFeatureParams": original_params_by_name.get(name, []),
        })

    return features


@st.dialog("Edit Model", width="large")
def edit_model_dialog(model):
    with st.form("edit_model_form"):
        st.caption("Changing Model Name creates a new model row instead of renaming this one (save matches on the original name).")
        model_name = st.text_input("Model Name", value=model.get("modelName", ""))
        col1, col2 = st.columns(2)
        version = col1.text_input("Version", value=model.get("version", ""), disabled=True)
        instrument = col2.text_input("Instrument", value=model.get("instrument", ""))

        col1, col2 = st.columns(2)
        training_start = col1.text_input("Training Start (YYYYMMDD)", value=model.get("trainingStart", ""))
        training_end = col2.text_input("Training End (YYYYMMDD)", value=model.get("trainingEnd", ""))

        col1, col2 = st.columns(2)
        target = col1.selectbox(
            "Target", TARGET_OPTIONS,
            index=TARGET_OPTIONS.index(model["target"]) if model.get("target") in TARGET_OPTIONS else 0,
        )
        bar_type = col2.selectbox(
            "Bar Type", BAR_TYPE_OPTIONS,
            index=BAR_TYPE_OPTIONS.index(model["barType"]) if model.get("barType") in BAR_TYPE_OPTIONS else 0,
        )

        col1, col2 = st.columns(2)
        window_start = col1.text_input("Window Start (HHmmss)", value=model.get("windowStart") or "")
        window_end = col2.text_input("Window End (HHmmss)", value=model.get("windowEnd") or "")

        col1, col2 = st.columns(2)
        export = col1.checkbox("Export", value=bool(model.get("export", False)))
        active = col2.checkbox("Active", value=bool(model.get("active", False)))

        st.markdown("**Model Features**")
        features_df = st.data_editor(
            build_features_df(model.get("modelFeatures")),
            num_rows="dynamic",
            width="stretch",
            key="features_editor",
            column_config={
                "Source": st.column_config.SelectboxColumn("Source", options=SOURCE_OPTIONS, required=True),
            },
        )

        col_save, col_cancel = st.columns(2)
        save_clicked = col_save.form_submit_button("Save", type="primary")
        cancel_clicked = col_cancel.form_submit_button("Cancel")

    if cancel_clicked:
        st.session_state["_close_edit_dialog"] = True
        st.rerun()

    if save_clicked:
        model_features = build_model_features_payload(features_df, model.get("modelFeatures"))
        payload = {
            "modelName": model_name,
            "version": version,
            "instrument": instrument,
            "trainingStart": training_start,
            "trainingEnd": training_end,
            "target": target,
            "barType": bar_type,
            "windowStart": window_start or None,
            "windowEnd": window_end or None,
            "featureLocation": model.get("featureLocation", ""),
            "outputLocation": model.get("outputLocation", ""),
            "export": export,
            "active": active,
            "modelFeatures": model_features,
        }
        try:
            response = requests.post(MODELS_ENDPOINT, json=payload)
            response.raise_for_status()
        except requests.RequestException as e:
            st.error(f"Failed to save model: {e}")
        else:
            st.session_state["_close_edit_dialog"] = True
            st.rerun()


tab_home, tab_models = st.tabs(["Home", "Models"])

with tab_home:
    pass

with tab_models:
    if st.session_state.pop("_close_edit_dialog", False):
        st.session_state[MODELS_GRID_KEY] = {"selection": {"rows": [], "columns": []}}

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
        ])
        df.insert(len(df.columns), "Edit", "✏️ Edit")

        event = st.dataframe(
            df,
            width="stretch",
            hide_index=True,
            on_select="rerun",
            selection_mode="single-row",
            key=MODELS_GRID_KEY,
            column_config={
                "Edit": st.column_config.TextColumn("Edit", disabled=True),
            },
        )

        selected_rows = event.selection.rows if event and event.selection else []
        if selected_rows:
            edit_model_dialog(models[selected_rows[0]])
