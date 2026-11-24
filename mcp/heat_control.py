"""Compatibility shim; control_heat now lives in workstation_tools."""
from workstation_tools import register

__all__ = ["register"]
