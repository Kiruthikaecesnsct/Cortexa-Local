from dataclasses import dataclass


@dataclass(frozen=True)
class AxisScores:
    novelty: int
    feasibility: int
    strategic: int
    patentability: int
