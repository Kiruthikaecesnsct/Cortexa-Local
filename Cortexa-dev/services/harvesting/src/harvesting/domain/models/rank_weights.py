from dataclasses import dataclass


@dataclass(frozen=True)
class RankWeights:
    novelty: float
    feasibility: float
    strategic: float
    patentability: float

    def __post_init__(self) -> None:
        weights = [self.novelty, self.feasibility, self.strategic, self.patentability]
        for w in weights:
            if not (0.0 < w <= 1.0):
                raise ValueError(f"Each weight must be in (0, 1], got {w}")
        total = sum(weights)
        if abs(total - 1.0) > 1e-9:
            raise ValueError(f"Weights must sum to 1.0, got {total}")
