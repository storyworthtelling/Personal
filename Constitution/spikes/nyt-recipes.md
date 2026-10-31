# Spike C-11.4.1 - NYT recipes: an official route, or none

Read on **2026-10-17**. Shapes and facts only; no personal data. Researcher reading only: no NYT page was
fetched by code, and no scraper was written or run.

## Outcome

**PERMISSION NEEDED.** No official NYT API covers recipes or Cooking, so there is no API route. The only
lawful route is NYT's prior written permission, which the owner has to ask for (letter below). Until a
written yes arrives, build as if the answer were **NO**.

## What I could and could not read (be sceptical of this file accordingly)

- **Could not read at source:** developer.nytimes.com, help.nytimes.com (Terms of Service) and
  cooking.nytimes.com/robots.txt. The research tool refused all three hosts ("unable to fetch"), and a
  search restricted to nytimes.com was rejected as inaccessible to the tool's crawler. So **every NYT
  fact below is second-hand** (a mirror or secondary page that cites NYT), and the API list is a
  third-party snapshot that may lag the live portal.
- **Not re-verified this session:** the robots.txt wording quoted in the technical finding (TF section
  1.7). That was read by an earlier session and is consistent with what secondary sources say, but I
  did not see it myself.
- **Not found at all:** NYT's separate *developer* terms of use (attribution, caching, commercial
  limits, rate limits). Search returned nothing usable. Rate-limit figures I saw conflict between
  sources, so none are recorded here.
- **Owner check, five minutes:** open developer.nytimes.com/apis and confirm the list below still has no
  Cooking or recipe entry. That is the one thing that could change the outcome to OFFICIAL ROUTE EXISTS.

## Evidence

### 1. The API list has no recipe API

Sources: https://jentic.com/apis/nytimes.com.md (11 APIs, a third-party catalogue of the NYT portal);
https://en.wikipedia.org/wiki/List_of_news_media_APIs (6 of them, older); web search for
"NYT Cooking API recipes official developer access".

The eleven: Books, Community (comments), Most Popular, Movie Reviews, Times Newswire, Semantic, Archive,
Article Search, Geographic, TimesTags, Top Stories. The Jentic page says plainly that none covers
recipes or cooking. Wikipedia's shorter list does not mention one either. A search for an official
Cooking API found only third-party scrapers and community connectors, one of which describes itself as
reading "the JSON endpoints the website itself uses" because there is "no official public API" (a
third-party claim, not NYT's).

What the APIs return: article **metadata** (headlines, abstracts, links), reviews, bestseller lists, tags.
None returns recipe content (ingredients, method). Article Search could in principle surface a Cooking
URL, but a URL is not a recipe, and following it with code is the scraping D82 forbids. (That Article
Search returns web links and snippets is my general knowledge, not read this session.)

### 2. The terms forbid automated access and software/AI use, absent written permission

Source (secondary copy of the NYT Terms of Service, quoted short):
https://libguides.lut.fi/terms/nyt ; coverage of the August 2023 terms change:
https://www.popsci.com/technology/nyt-generative-ai/ and
https://www.mediapost.com/publications/article/388219 . Primary source to confirm:
https://help.nytimes.com/hc/en-us/articles/115014893428-Terms-of-Service (not readable here).

- Content is "intended for your personal, non-commercial use."
- Bars "any manual or automatic device, tool, or process designed to data mine or scrape the Content."
- Bars using content "for the development of any software program, model, algorithm, or generative AI
  tool"; non-commercial use "does not include" use "without prior written consent."
- "Copying or storing of any Content for other than personal use is expressly prohibited"; no caching
  or archiving of content.
- "robots.txt notice does not constitute NYT's prior written consent" (so the robots.txt wording in
  TF section 1.7 and the terms point the same way).
- Permission for uses beyond personal use goes to NYT's Rights and Permissions department (named on
  the terms page; I did not capture its address, so the letter leaves it as a placeholder).

### 3. Personal and household use does not rescue the planner

"Personal, non-commercial use" covers a person reading a recipe. It does **not** cover what Constitution
would do: store recipe text in the box, hand it to Claude (taste profile, planning, step re-timing) and
reprint it on a sheet. Those are the software/AI/storage uses the terms name. This is the same
conclusion as TF section 1.7. An API alone would not have settled it either; any route also needs
permission for the AI step, so the letter asks for that explicitly.

## Effect on the plan

- **PC8 (page 14, "Paste an NYT Cooking link" row): not built.** The picker shows the scan row alone,
  and the omission goes back to the owner as a book revision (PC8's own wording). Revisit only if NYT
  replies yes in writing.
- **C-6.3.4 (the NYT stretch dish): close unbuilt, and tell the owner.** The weekly stretch dish stays
  "a household card not cooked lately" (D82's interim). `NoScrapingTests` still ships (G25): it is
  cheap and keeps the promise honest.
- **If permission is granted:** build only to its stated scope (probably owner-pasted single links, with
  an explicit allowance for the AI steps), and re-open PC8 and C-6.3.4 with the letter's wording quoted.
  A grant that does not mention AI/LLM use is not enough.

## Draft permission request (for the owner to send; placeholder sender)

> To: The New York Times, Rights and Permissions  (address: to be taken from the Terms of Service page)
> From: Owner, owner@example.com
> Subject: Request for written permission - personal household recipe planner using NYT Cooking
>
> Hello,
>
> I am a subscriber to NYT Cooking. I am building a small, private, non-commercial app for my own
> household (two adults) that plans weekly dinners. Your Terms of Service say content is for personal,
> non-commercial use and that automated tools and software or AI use need prior written consent, so I am
> asking before building anything.
>
> What I would like to do, for household use only, with no redistribution and no public release:
> 1. When I paste the link to a single recipe I am subscribed to read, the app reads that one page once
>    and keeps the ingredients and steps in our private household recipe box.
> 2. The app may pass that recipe text to an AI service (Anthropic's Claude) to suggest a weekly menu,
>    adjust to our tastes and re-time the steps. The text is not used to train any model.
> 3. Optionally, once a week the app suggests one NYT Cooking recipe we have not tried.
>
> Please tell me whether you can permit this, an official API or feed that I should use instead, and any
> conditions (attribution, retention, number of recipes). I will not do any of this until I have your
> written reply.
>
> Thank you,
> Owner
> owner@example.com

## Owner action (already listed in plan section 10)

Send the letter above if the portal check in "What I could and could not read" still shows no Cooking
API. A reply is not expected soon; the plan does not wait on it.
