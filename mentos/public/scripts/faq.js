        const faqSearch = document.getElementById("faqSearch");
        const faqCategories = [...document.querySelectorAll(".faq-category")];
        const faqItems = [...document.querySelectorAll(".faq-item")];
        const faqCount = document.getElementById("faqCount");
        const noResults = document.getElementById("noResults");

        function filterFaqs() {
            const query = faqSearch.value.trim().toLocaleLowerCase();
            let visibleCount = 0;

            faqCategories.forEach((category) => {
                const items = [...category.querySelectorAll(".faq-item")];
                let categoryCount = 0;

                items.forEach((item) => {
                    const matches = item.textContent.toLocaleLowerCase().includes(query);
                    item.hidden = !matches;
                    if (matches) {
                        categoryCount += 1;
                        visibleCount += 1;
                    } else {
                        item.open = false;
                    }
                });

                category.hidden = categoryCount === 0;
            });

            faqCount.textContent = query
                ? `${visibleCount} ${visibleCount === 1 ? "question" : "questions"} found`
                : `${faqItems.length} questions across ${faqCategories.length} topics`;
            noResults.hidden = visibleCount > 0;
        }

        faqSearch.addEventListener("input", filterFaqs);
        filterFaqs();
