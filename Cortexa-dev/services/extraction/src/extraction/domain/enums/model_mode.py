from enum import StrEnum


class ModelMode(StrEnum):
    single_primary = "single_primary"
    single_secondary = "single_secondary"
    dual_adversarial = "dual_adversarial"
