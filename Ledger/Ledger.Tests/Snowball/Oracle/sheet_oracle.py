#!/usr/bin/env python3
"""Independent oracle for guard G7 (SnowballSheetTests): the Debt Snowball sheet's "Loans Today" formulas.

REGENERATE (stdlib only, deterministic; run from the repository root):

    python3 Ledger/Ledger.Tests/Snowball/Oracle/sheet_oracle.py > Ledger/Ledger.Tests/Snowball/Oracle/sheet_expected.json

The output is committed as sheet_expected.json and embedded in the test assembly, so the test needs no
Python at run time. The inputs below are INVENTED (fictional lenders, made-up figures); only the SHAPE
is the owner's sheet: ten loans in focus order, the first three already paid off, a mortgage-like loan
last, whole-week dates.

Why this file exists: the guard must not check SnowballEngine against SnowballEngine. This oracle is
written from the formulas as INDEX-P11-TECHNICAL-FINDING.md Appendix A documents them and shares no code
or approach with the engine. In particular the weeks-to-payoff is found by EXACT rational arithmetic
(pay one week at a time until the balance is cleared), not by the closed-form logarithm the engine
uses, so there is no floating-point dust and no epsilon to get wrong. The log form (Excel's NPER) is
computed alongside only as a cross-check, and the script refuses to write anything where the two
disagree or where a value sits within 1e-6 of a whole week (a boundary a reader could round either way).

The formulas (Appendix A, "Loans Today"), for row n of the loans in focus order:
  E  focused weekly payment   row 1: D1 (+ the plan's one overall extra weekly payment); row n: E(n-1) + D(n)
  F  weeks before focused     row 1: 0;  row n: H(n-1) + F(n-1)
  G  amount before focused    B(n) - D(n) * F(n) * (1 - C(n)), rounded to cents (half up)
                              (interest before a loan is the focus is a share of the PAYMENT, not the balance;
                               the sheet's row 2 multiplies by E rather than D, harmless only because F is 0 there)
  H  weeks to payoff          0 when G = 0, else NPER(C/52, -E, G) rounded UP to whole weeks
                              (after it becomes the focus the loan amortises properly at APR/52)
  I  payoff date              last payment date + 7 * (sum of H up to and including this row): whole weeks
Totals: balance = sum(B); weeks = sum(H) = F(last) + H(last); debt-free date = I(last).
"""
import json
import math
import sys
from datetime import date, timedelta
from decimal import Decimal, ROUND_HALF_UP
from fractions import Fraction

CENT = Decimal("0.01")


def money(text):
    return Decimal(text)


# ---- Scenario inputs (INVENTED). (name, balance B, APR C as a yearly fraction, weekly payment D) ----
TEN_LOANS = [
    ("Northfield Student Loans 1", "0.00", "0.0550", "150"),       # already paid off
    ("Northfield Student Loans 2", "0.00", "0.0540", "150"),       # already paid off
    ("Cedar Hollow Bank", "0.00", "0.1650", "75"),                 # already paid off
    ("Meridian Card", "7412.36", "0.2650", "90"),                  # the first live loan, focus from week 0
    ("Skyline Miles", "11238.90", "0.2450", "80"),
    ("Bluebird Visa", "13905.25", "0.2150", "85"),
    ("Lakeside Credit Union", "15620.40", "0.0350", "40"),         # low rate, slow share of its payment
    ("Harbor Auto Finance", "24912.40", "0.0690", "140"),
    ("Cobblestone Finance", "36402.80", "0.0745", "130"),
    ("Maple Ridge Mortgage", "171350.00", "0.0425", "310"),        # mortgage-like: last in the focus order
]

# Row 1 is live (F = 0, so the sheet's E-for-D quirk cannot show) and the plan carries an extra weekly
# payment that is added to row 1 only; a 0% promotional loan has no logarithm to take.
EXTRA_AND_PROMO = [
    ("Bluebird Visa", "5200.00", "0.2299", "60"),
    ("Cobblestone Finance", "9000.00", "0.0000", "50"),
    ("Harbor Auto Finance", "14000.00", "0.0690", "140"),
    ("Lakeside Credit Union", "14800.75", "0.0475", "55"),
]

# The paid-off loan comes FIRST while an extra payment is in play: the extra still lands on row 1's focused
# payment and rolls down, and the paid-off row adds no weeks and moves no date.
PAID_OFF_FIRST_WITH_EXTRA = [
    ("Northfield Student Loans 1", "0.00", "0.0550", "120"),
    ("Meridian Card", "3050.00", "0.2650", "45"),
    ("Skyline Miles", "8800.40", "0.2450", "70"),
]

SCENARIOS = [
    ("ten loans, three paid off, mortgage last", date(2027, 3, 19), "0", TEN_LOANS),
    ("live first loan with an extra weekly payment and a 0% loan", date(2027, 3, 19), "35", EXTRA_AND_PROMO),
    ("paid-off first loan with an extra weekly payment", date(2027, 3, 19), "20", PAID_OFF_FIRST_WITH_EXTRA),
]


def weeks_exact(pv, apr, pmt):
    """Smallest whole number of weekly payments that clears pv, by exact rational arithmetic."""
    if pv == 0:
        return 0
    rate = Fraction(apr) / 52
    balance = Fraction(pv)
    payment = Fraction(pmt)
    weeks = 0
    while balance > 0:
        balance = balance * (1 + rate) - payment   # interest accrues for the week, then the payment lands
        weeks += 1
        if weeks > 5000:
            raise SystemExit("loan never pays off: payment does not cover the weekly interest")
    return weeks


def weeks_nper(pv, apr, pmt):
    """Excel NPER(rate, -pmt, pv) rounded up, as a cross-check only."""
    if pv == 0:
        return 0, 0.0
    rate = float(apr) / 52
    if rate == 0:
        raw = float(pv) / float(pmt)
    else:
        raw = -math.log(1 - rate * float(pv) / float(pmt)) / math.log(1 + rate)
    return math.ceil(raw), raw


def compute(loans, last_payment, extra):
    rows = []
    e = Decimal(0)
    h_prev = 0
    f_prev = 0
    running = last_payment
    for n, (name, b, c, d) in enumerate(loans):
        b, c, d = money(b), money(c), money(d)
        e = d + money(extra) if n == 0 else e + d
        f = 0 if n == 0 else h_prev + f_prev
        g = (b - d * f * (1 - c)).quantize(CENT, rounding=ROUND_HALF_UP)
        if g < 0:
            raise SystemExit(f"{name}: its own payments would clear it before it is the focus (G < 0); "
                             "the sheet defines nothing for that, so the oracle refuses to invent it")
        h = weeks_exact(g, c, e)
        nper_h, raw = weeks_nper(g, c, e)
        if h != nper_h:
            raise SystemExit(f"{name}: exact weeks {h} disagree with NPER {nper_h}")
        if g != 0 and abs(raw - round(raw)) < 1e-6:
            raise SystemExit(f"{name}: NPER {raw} is within 1e-6 of a whole week; pick other inputs")
        running = running + timedelta(days=7 * h)
        rows.append({"name": name, "e": str(e), "f": f, "g": str(g), "h": h, "i": running.isoformat()})
        h_prev, f_prev = h, f
    return rows


def build():
    out = {
        "about": "Generated by sheet_oracle.py from INVENTED inputs; do not edit by hand.",
        "scenarios": [],
    }
    for title, last_payment, extra, loans in SCENARIOS:
        rows = compute(loans, last_payment, extra)
        out["scenarios"].append({
            "title": title,
            "lastPaymentDate": last_payment.isoformat(),
            "extraWeeklyPayment": extra,
            "loans": [{"name": n, "balance": b, "apr": c, "weeklyPayment": d} for n, b, c, d in loans],
            "rows": rows,
            "totalBalance": str(sum((money(b) for _, b, _, _ in loans), Decimal(0))),
            "totalWeeks": sum(r["h"] for r in rows),
            "debtFreeDate": rows[-1]["i"],
        })
    return out


if __name__ == "__main__":
    json.dump(build(), sys.stdout, indent=2)
    sys.stdout.write("\n")
