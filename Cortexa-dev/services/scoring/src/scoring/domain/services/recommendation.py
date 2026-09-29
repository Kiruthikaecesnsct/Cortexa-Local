PURSUE_THRESHOLD = 70.0
INVESTIGATE_THRESHOLD = 40.0


def recommendation_for(score: float) -> str:
    if score >= PURSUE_THRESHOLD:
        return "pursue"
    if score >= INVESTIGATE_THRESHOLD:
        return "investigate"
    return "abandon"
